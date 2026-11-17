"""Desktop integration for macOS and Linux: tray/menu-bar icon, notifications, sounds,
launch at sign-in and single instance."""
from __future__ import annotations

import os
import plistlib
import shlex
import sys
from pathlib import Path
from typing import Callable, Optional

from PyQt6.QtCore import QRectF, QUrl, Qt
from PyQt6.QtGui import QAction, QColor, QIcon, QPainter, QPixmap
from PyQt6.QtNetwork import QLocalServer, QLocalSocket
from PyQt6.QtWidgets import QMenu, QSystemTrayIcon

from ..core.alerts import AlertEvent
from ..core.settings import RadarSettings, ReminderSeverity
from . import theme as T

APP_ID = "ai-token-scrooge"
LAUNCH_AGENT = "com.github.ai-token-scrooge"


# ---------------------------------------------------------------- sounds

class Sounds:
    """Ducks / Classic themes. Refresh uses the dismiss cue. Previews ignore the master switch."""
    CUES = {"alert": "alert", "snooze": "snooze", "dismiss": "dismiss", "info": "info", "refresh": "dismiss"}

    def __init__(self):
        self._effects: dict[str, object] = {}
        try:
            from PyQt6.QtMultimedia import QSoundEffect
            self._factory = QSoundEffect
        except ImportError:  # QtMultimedia is optional on some Linux distributions.
            self._factory = None

    @property
    def available(self) -> bool:
        return self._factory is not None

    def play(self, settings: RadarSettings, action: str, preview: bool = False) -> None:
        if self._factory is None or (not preview and not settings.Sound):
            return
        cue = self.CUES.get(action)
        if cue is None:
            return
        theme = "classic" if settings.SoundTheme == "Classic" else "duck"
        name = f"{theme}-{cue}.wav"
        effect = self._effects.get(name)
        if effect is None:
            effect = self._factory()
            effect.setSource(QUrl.fromLocalFile(T.resource_path("sounds", name)))
            self._effects[name] = effect
        effect.setVolume(max(0.0, min(100.0, float(settings.SoundVolume))) / 100)
        effect.play()


# ---------------------------------------------------------------- tray and notifications

def tray_icon(alert: Optional[AlertEvent]) -> QIcon:
    """App icon with a severity badge when Alert on Icon is enabled and an alert is active."""
    base = T.pixmap("token_baron.png", 64, ratio=1)
    if alert is None:
        return QIcon(base)
    pm = QPixmap(base)
    p = QPainter(pm)
    p.setRenderHint(QPainter.RenderHint.Antialiasing)
    color = T.CRITICAL if alert.Severity == ReminderSeverity.Critical else T.WARNING if alert.Severity == ReminderSeverity.Warning else T.INFO
    p.setPen(QColor(T.CANVAS))
    p.setBrush(QColor(color))
    p.drawEllipse(QRectF(36, 36, 26, 26))
    p.end()
    return QIcon(pm)


class Tray:
    def __init__(self, open_app: Callable[[], None], refresh: Callable[[], None], exit_app: Callable[[], None]):
        self.problem: Optional[str] = None
        self.icon = QSystemTrayIcon(tray_icon(None))
        self.icon.setToolTip("AI Token Scrooge")
        menu = QMenu()
        for text, action in (("Open Scrooge", open_app), ("Refresh Now", refresh)):
            item = QAction(text, menu)
            item.triggered.connect(action)
            menu.addAction(item)
        menu.addSeparator()
        quit_action = QAction("Exit", menu)
        quit_action.triggered.connect(exit_app)
        menu.addAction(quit_action)
        self.menu = menu
        self.icon.setContextMenu(menu)
        self._open = open_app
        self.icon.activated.connect(self._activated)
        self.icon.messageClicked.connect(open_app)
        self.available = QSystemTrayIcon.isSystemTrayAvailable()
        if self.available:
            self.icon.show()

    def _activated(self, reason) -> None:
        if reason in (QSystemTrayIcon.ActivationReason.Trigger, QSystemTrayIcon.ActivationReason.DoubleClick):
            self._open()

    def set_status(self, tooltip: str, alert: Optional[AlertEvent]) -> None:
        self.icon.setToolTip(tooltip)
        self.icon.setIcon(tray_icon(alert))

    def show(self, alert: AlertEvent, provider: str, force: bool = False) -> bool:
        """Returns whether the desktop accepted the message. Content is title/detail only, never
        credentials or raw provider data."""
        if not self.available or not QSystemTrayIcon.supportsMessages():
            self.problem = "Desktop notifications are unavailable. Alerts still appear inside Scrooge."
            return False
        self.problem = None
        severity = {ReminderSeverity.Critical: QSystemTrayIcon.MessageIcon.Critical,
                    ReminderSeverity.Warning: QSystemTrayIcon.MessageIcon.Warning}.get(alert.Severity, QSystemTrayIcon.MessageIcon.Information)
        heading = "OpenAI Codex" if provider == "OpenAI" else provider
        self.icon.showMessage(f"{heading} · {alert.Title}", alert.Detail, severity, 10_000)
        return True

    def hide(self) -> None:
        self.icon.hide()


# ---------------------------------------------------------------- launch at sign-in

def _launch_command(data_dir: Optional[Path] = None) -> list[str]:
    base = [sys.executable] if getattr(sys, "frozen", False) else [sys.executable, "-m", "token_scrooge"]
    # Sign-in launches do not inherit shell variables, so a SCROOGE_DATA_DIR folder is written explicitly.
    return base + ["--background"] + (["--data-dir", str(data_dir)] if data_dir is not None else [])


def _autostart_path() -> Path:
    if sys.platform == "darwin":
        return Path.home() / "Library" / "LaunchAgents" / f"{LAUNCH_AGENT}.plist"
    base = Path(os.environ.get("XDG_CONFIG_HOME") or Path.home() / ".config")
    return base / "autostart" / f"{APP_ID}.desktop"


def set_launch_at_login(enabled: bool, data_dir: Optional[Path] = None) -> None:
    """Write or remove the per-user autostart entry. Raises OSError when it cannot be changed.
    `data_dir` is included in the launch command when the folder is not the platform default."""
    path = _autostart_path()
    if not enabled:
        path.unlink(missing_ok=True)
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    command = _launch_command(data_dir)
    if sys.platform == "darwin":
        data = plistlib.dumps({"Label": LAUNCH_AGENT, "ProgramArguments": command, "RunAtLoad": True,
                               "ProcessType": "Interactive"})
    else:
        data = ("[Desktop Entry]\nType=Application\nName=AI Token Scrooge\n"
                f"Exec={shlex.join(command)}\nX-GNOME-Autostart-enabled=true\nTerminal=false\n").encode()
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_bytes(data)
    os.replace(tmp, path)


def launch_at_login_enabled() -> bool:
    return _autostart_path().exists()


# ---------------------------------------------------------------- single instance

class SingleInstance:
    """The first instance listens on a per-user local socket; later launches ask it to show
    its window and exit."""

    def __init__(self, data_dir: Path, activate: Callable[[], None]):
        import hashlib
        key = hashlib.sha256(str(data_dir.resolve()).encode()).hexdigest()[:16]
        self.name = f"{APP_ID}-{os.getuid()}-{key}"
        self._activate = activate
        self.server: Optional[QLocalServer] = None

    def claim(self) -> bool:
        socket = QLocalSocket()
        socket.connectToServer(self.name)
        if socket.waitForConnected(300):
            socket.write(b"show")
            socket.flush()
            socket.waitForBytesWritten(300)
            socket.disconnectFromServer()
            return False
        QLocalServer.removeServer(self.name)
        self.server = QLocalServer()
        self.server.setSocketOptions(QLocalServer.SocketOption.UserAccessOption)
        self.server.newConnection.connect(self._connection)
        self.server.listen(self.name)
        return True

    def _connection(self) -> None:
        while self.server and self.server.hasPendingConnections():
            connection = self.server.nextPendingConnection()
            connection.readyRead.connect(lambda c=connection: (c.readAll(), self._activate(), c.disconnectFromServer()))
