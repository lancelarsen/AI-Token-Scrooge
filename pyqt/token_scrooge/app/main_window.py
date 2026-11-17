"""Main window (port of MainPage): header, Reset Horizon, account cards, alert banner, panels."""
from __future__ import annotations

from datetime import datetime, timezone
from typing import Optional

from PyQt6.QtCore import QRectF, Qt, QTimer
from PyQt6.QtGui import QColor, QPainter, QPen
from PyQt6.QtWidgets import (QFrame, QHBoxLayout, QLabel, QMainWindow, QScrollArea, QVBoxLayout, QWidget)

from ..core.alerts import AlertEvent
from ..core.labels import updated_label
from ..core.model import ProviderAccount, QuotaSnapshot
from ..core.settings import ReminderSeverity
from . import desktop
from . import theme as T
from .controller import AppController
from .dashboard import AccountsView
from .dialogs import (AlertHistoryPanel, BrowserConnectPanel, CodexConnectPanel, GitHubConnectPanel, choose_provider,
                      show_details, show_reserve)
from .panels import PanelHost, message
from .widgets import ActionButton, hline, label


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


class Banner(QFrame):
    """In-app alert: provider, severity-colored title, detail, Show Info / Snooze 1 Hour / Dismiss."""

    def __init__(self, alert: AlertEvent, provider: str, parent: QWidget, show_info, snooze, dismiss):
        super().__init__(parent)
        self.alert = alert
        self.color = {ReminderSeverity.Critical: T.CRITICAL, ReminderSeverity.Warning: T.WARNING,
                      ReminderSeverity.Info: T.INFO}.get(alert.Severity, T.HIGHLIGHT)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(18, 18, 18, 18)
        layout.setSpacing(10)
        heading = QHBoxLayout()
        heading.setSpacing(12)
        logo = QLabel()
        logo.setPixmap(T.provider_pixmap(provider, 38))
        heading.addWidget(logo)
        titles = QVBoxLayout()
        titles.setSpacing(3)
        titles.addWidget(label("OpenAI Codex" if provider == "OpenAI" else provider, 12, T.HIGHLIGHT))
        titles.addWidget(label(alert.Title, 15, self.color, True, wrap=True))
        heading.addLayout(titles, 1)
        layout.addLayout(heading)
        layout.addWidget(label(alert.Detail, 12, T.TEXT_SECONDARY, wrap=True))
        actions = QHBoxLayout()
        actions.setSpacing(8)
        for text, action in (("Show Info", show_info), ("Snooze 1 Hour", snooze), ("Dismiss", dismiss)):
            button = ActionButton(text)
            button.clicked.connect(action)
            actions.addWidget(button)
        actions.addStretch(1)
        layout.addLayout(actions)
        self.setFixedWidth(520)

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(QPen(QColor(self.color), 1.5))
        p.setBrush(QColor(T.DIALOG))
        p.drawRoundedRect(QRectF(self.rect()).adjusted(1, 1, -1, -1), 14, 14)


class MainWindow(QMainWindow):
    def __init__(self, controller: AppController, tray: Optional[desktop.Tray], sounds: desktop.Sounds,
                 compact: bool = False):
        super().__init__()
        self.controller, self.tray, self.sounds = controller, tray, sounds
        self.exiting = False
        self.autostart_allowed = True
        self.autostart_data_dir = None  # Set when the data folder came from SCROOGE_DATA_DIR.
        self.banner: Optional[Banner] = None
        self.highlighted: Optional[str] = None
        self.setWindowTitle("AI Token Scrooge")
        self.setWindowIcon(T.app_icon())
        self.setMinimumSize(420, 520)
        self.resize(900, 900) if compact else self.resize(1280, 960)

        canvas = QWidget()
        canvas.setObjectName("Canvas")
        self.setCentralWidget(canvas)
        root = QVBoxLayout(canvas)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)

        # ---------------------------------------------------- header
        self.header = QWidget()
        self.header_layout = QVBoxLayout(self.header)
        self.header_layout.setContentsMargins(32, 18, 32, 18)
        self.brand = QWidget()
        brand = QHBoxLayout(self.brand)
        brand.setContentsMargins(0, 0, 0, 0)
        brand.setSpacing(14)
        logo = QLabel()
        logo.setPixmap(T.pixmap("token_baron.png", 66))
        logo.setFixedSize(66, 66)
        brand.addWidget(logo)
        names = QVBoxLayout()
        names.setSpacing(0)
        names.addWidget(label("AI Token Scrooge", 32, T.TEXT, True))
        names.addWidget(label("Never Waste a Token! Never Miss a Reset!", 16, T.HIGHLIGHT))
        brand.addLayout(names)
        brand.addStretch(1)
        self.actions = QWidget()
        actions = QHBoxLayout(self.actions)
        actions.setContentsMargins(0, 0, 0, 0)
        actions.setSpacing(14)
        self.freshness = label("", 12, T.TEXT_SECONDARY, align=Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter)
        actions.addWidget(self.freshness, 1)
        self.refresh_button = ActionButton("Refresh", "refresh")
        self.refresh_button.clicked.connect(self.refresh_by_user)
        self.settings_button = ActionButton("Settings", "settings")
        self.settings_button.clicked.connect(lambda: self.open_settings())
        actions.addWidget(self.refresh_button)
        actions.addWidget(self.settings_button)
        self.header_row = QHBoxLayout()
        self.header_layout.addLayout(self.header_row)
        self._header_wide: Optional[bool] = None
        root.addWidget(self.header)
        root.addWidget(hline(T.HEADER_DIVIDER))

        # ---------------------------------------------------- body
        self.scroll = QScrollArea()
        self.scroll.setWidgetResizable(True)
        self.scroll.setFrameShape(QFrame.Shape.NoFrame)
        self.scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        body = QWidget()
        self.body = QVBoxLayout(body)
        self.body.setContentsMargins(32, 24, 32, 32)
        self.body.setSpacing(20)
        self.horizon = self._make_horizon()
        if self.horizon is not None:
            self.body.addWidget(self.horizon)
        self.accounts = AccountsView(controller, {
            "details": self.open_account, "reserve": lambda account_id: self.open_reserve(account_id),
            "account": self.open_account_settings, "add": self.add_account, "demo": controller.load_demo,
            "exit_demo": controller.exit_demo, "error": lambda t, m: message(self.panels, t, m)})
        self.body.addWidget(self.accounts)
        self.body.addStretch(1)
        self.scroll.setWidget(body)
        root.addWidget(self.scroll, 1)

        self.panels = PanelHost(canvas)
        self.panels.opened_changed.connect(lambda open_: None if open_ else self.render())

        controller.changed.connect(self.render)
        controller.clock.connect(self.tick)
        controller.alert.connect(self.show_banner)
        controller.refreshing_changed.connect(self._refreshing)
        controller.status.connect(lambda text: self.freshness.setText(text))
        self._layout_header()

    def _make_horizon(self):
        try:
            from .timeline import ResetHorizon
        except ImportError:
            return None
        return ResetHorizon(self.controller, self.open_event, lambda: self.open_settings("History and Alerts"))

    # ---------------------------------------------------- layout
    def _layout_header(self) -> None:
        wide = self.width() >= 1100
        if wide == self._header_wide:
            return
        self._header_wide = wide
        while self.header_row.count():
            self.header_row.takeAt(0)
        while self.header_layout.count() > 1:
            self.header_layout.takeAt(1)
        if wide:
            self.actions.setFixedWidth(500)
            self.header_row.addWidget(self.brand, 1)
            self.header_row.addWidget(self.actions)
        else:
            self.actions.setMinimumWidth(0)
            self.actions.setMaximumWidth(16777215)
            self.header_row.addWidget(self.brand, 1)
            self.header_layout.addWidget(self.actions)
            self.header_layout.setSpacing(12)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        self._layout_header()
        self._place_banner()

    # ---------------------------------------------------- render
    def render(self) -> None:
        c = self.controller
        state = c.state
        if state.is_demo:
            text = "DEMO DATA · Live monitoring paused"
        elif not state.snapshots:
            text = "Connect an account to start monitoring"
        else:
            text = updated_label(max(s.ObservedAt for s in state.snapshots.values()), c.zone)
        if c.retired and not state.is_demo:
            text += " · Previous sign-in cleanup pending"
        self.freshness.setText(text)
        if self.panels.is_open:
            return  # Keep the covered dashboard stable; render again when panels close.
        self.accounts.render()
        if self.highlighted:
            self.accounts.highlight(self.highlighted)
        if self.horizon is not None:
            self.horizon.reload()
        self._update_tray()

    def tick(self) -> None:
        self.accounts.update_clock()
        if self.horizon is not None:
            self.horizon.tick()
        self._update_tray()

    def _update_tray(self) -> None:
        if self.tray is None:
            return
        status = self.controller.monitor_status()
        alert = self.controller.icon_alert() if self.controller.settings.AlertOnIcon else None
        tooltip = "AI Token Scrooge"
        if self.controller.state.is_demo:
            tooltip += "\nDemo Data"
        elif status.Notice:
            tooltip += "\n" + status.Notice
        elif status.Capacity.name != "Unknown":
            tooltip += "\nLowest Capacity: " + {"Healthy": "Healthy", "Info": "Info", "Low": "Warning",
                                                "Critical": "Critical", "Exhausted": "Exhausted"}[status.Capacity.name]
        self.tray.set_status(tooltip[:120], alert)

    def _refreshing(self, busy: bool) -> None:
        if not busy and self.refresh_button.busy:
            self.refresh_button.set_busy(None)

    # ---------------------------------------------------- actions
    def refresh_by_user(self) -> None:
        if self.refresh_button.busy:
            return
        self.refresh_button.set_busy("Refreshing…")
        self.controller.play("refresh")
        self.controller.refresh(force=True, done=lambda: self.refresh_button.set_busy(None))

    def add_account(self) -> None:
        def chosen(provider: str):
            self.connect_account(provider, None)
        choose_provider(self.panels, chosen)

    def connect_account(self, provider: str, reconnect: Optional[ProviderAccount]) -> None:
        monitor = self.controller.monitor

        def connected(account: ProviderAccount, snapshot: QuotaSnapshot):
            self.controller.accept_account(account, snapshot)
        if provider == "OpenAI":
            panel = CodexConnectPanel(monitor, connected, reconnect)
        elif provider == "GitHub Copilot":
            panel = GitHubConnectPanel(monitor, connected, reconnect)
        else:
            panel = BrowserConnectPanel(provider, monitor, connected, reconnect)
        self.panels.show_panel(panel)

    def open_settings(self, focus_section: Optional[str] = None) -> None:
        from .settings_panel import GlobalSettingsPanel
        self.panels.show_panel(GlobalSettingsPanel(self.controller, self.open_alert_history, self.test_notification,
                                                   self.set_launch_at_login, focus_section=focus_section))

    def open_account_settings(self, account_id: str) -> None:
        if self.controller.state.account(account_id) is None:
            return
        from .settings_panel import AccountSettingsPanel
        self.panels.show_panel(AccountSettingsPanel(self.controller, account_id,
                                                    lambda account: self.connect_account(account.Provider, account),
                                                    self.remove_account))

    def remove_account(self, account: ProviderAccount) -> None:
        """Persists the removal (raises on failure); the account panel closes itself afterwards."""
        self.controller.remove_account(account.Id)

    def set_launch_at_login(self, enabled: bool) -> None:
        """Raises OSError when the autostart entry cannot be changed; the panel reverts the switch.
        Runs with --data-dir are isolated test folders and never change the sign-in entry."""
        if self.controller.state.is_demo:
            return
        if not self.autostart_allowed:
            raise OSError("Launch at sign-in is unavailable for a --data-dir run.")
        desktop.set_launch_at_login(enabled, self.autostart_data_dir)

    def open_account(self, account_id: str, window_id: Optional[str] = None) -> None:
        state = self.controller.state
        snapshot = state.snapshots.get(account_id)
        if not window_id and state.account(account_id) and snapshot is not None and snapshot.Reserve is not None:
            self.open_reserve(account_id)
            return
        row = next((r for r in state.rows() if r.Account.Id == account_id and (not window_id or r.Window.Id == window_id)), None)
        if row is None:
            message(self.panels, "Usage Details Unavailable", "This account or quota window is no longer available. Refresh the "
                    "dashboard to check current connections.")
            return
        show_details(self.panels, row, self.controller.zone, state.is_demo)

    def open_reserve(self, account_id: Optional[str] = None) -> None:
        show_reserve(self.panels, self.controller.state, self.controller.zone, account_id, utcnow())

    def open_event(self, event) -> None:
        if event.IsExpiry:
            self.open_reserve(event.AccountId)
        else:
            self.open_account(event.AccountId, event.WindowId)

    def open_alert_history(self) -> None:
        self.panels.show_panel(AlertHistoryPanel(self.controller, self.show_alert_account))

    def test_notification(self) -> None:
        try:
            self.controller.test_notification()
        except Exception:  # noqa: BLE001
            message(self.panels, "Local History Unavailable", "Scrooge could not record the test notification. Check available "
                    "space and folder access, then try again.")
            return
        if self.tray is not None and self.tray.problem:
            message(self.panels, "Notification Test", self.tray.problem)

    # ---------------------------------------------------- alerts
    def show_banner(self, alert: AlertEvent) -> None:
        if self.banner is not None:
            self.banner.deleteLater()
        account = self.controller.state.account(alert.AccountId)
        provider = account.Provider if account else "Scrooge"
        banner: Optional[Banner] = None

        def dismiss_this():
            if self.banner is banner:
                self.banner.deleteLater()
                self.banner = None

        def show_info():
            self.controller.play("info")
            dismiss_this()
            self.show_alert_account(alert.AccountId)

        def snooze():
            try:
                self.controller.snooze(alert.AccountId)
            except Exception:  # noqa: BLE001
                message(self.panels, "Snooze Not Saved", "Alerts remain active. Check available space and folder access, then try Snooze again.")
                return
            self.controller.play("snooze")
            dismiss_this()

        def dismiss():
            self.controller.play("dismiss")
            dismiss_this()
        banner = Banner(alert, provider, self.centralWidget(), show_info, snooze, dismiss)
        self.banner = banner
        banner.show()
        self._place_banner()
        banner.raise_()
        if self.panels.is_open:
            self.panels.raise_()

    def _place_banner(self) -> None:
        if self.banner is None:
            return
        parent = self.centralWidget()
        self.banner.adjustSize()
        w = min(520, parent.width() - 40)
        self.banner.setFixedWidth(w)
        h = self.banner.heightForWidth(w) if self.banner.hasHeightForWidth() else self.banner.sizeHint().height()
        self.banner.setGeometry(parent.width() - w - 20, parent.height() - h - 20, w, h)

    def show_alert_account(self, account_id: str) -> None:
        self.show_main()
        self.panels.close_all()
        self.highlighted = account_id
        self.render()
        card = self.accounts.highlight(account_id)
        if card is not None:
            QTimer.singleShot(50, lambda: self.scroll.ensureWidgetVisible(card, 0, 120))

        def clear():
            if self.highlighted == account_id:
                self.highlighted = None
                self.accounts.highlight(None)
        QTimer.singleShot(6000, clear)

    # ---------------------------------------------------- window
    def show_main(self) -> None:
        was_hidden = not self.isVisible() or self.isMinimized()
        self.showNormal() if self.isMinimized() else self.show()
        self.raise_()
        self.activateWindow()
        if was_hidden and not self.controller.state.is_demo:
            QTimer.singleShot(200, lambda: self.controller.refresh(force=True))

    def exit_app(self) -> None:
        self.exiting = True
        self.close()

    def closeEvent(self, event):
        if not self.exiting and self.controller.settings.CloseToTray and self.tray is not None and self.tray.available:
            event.ignore()
            self.hide()
            return
        self.controller.shutdown()
        if self.tray is not None:
            self.tray.hide()
        event.accept()
        from PyQt6.QtWidgets import QApplication
        QApplication.quit()

    def keyPressEvent(self, event):
        if event.key() == Qt.Key.Key_F5:
            self.refresh_by_user()
            return
        super().keyPressEvent(event)
