"""Entry point: python -m token_scrooge [--demo] [--data-dir PATH] [--compact] [--background]"""
from __future__ import annotations

import argparse
import signal
import sys
from pathlib import Path


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="token-scrooge", description="AI Token Scrooge for macOS and Linux")
    parser.add_argument("--demo", action="store_true", help="show built-in demo accounts; live monitoring is paused")
    parser.add_argument("--data-dir", type=Path,
                        help="use an isolated data folder for this run (overrides SCROOGE_DATA_DIR; for testing and screenshots)")
    parser.add_argument("--compact", action="store_true", help="open at a compact window size")
    parser.add_argument("--background", "--tray", action="store_true", help="start hidden in the tray or menu bar")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    from PyQt6.QtCore import Qt, QTimer
    from PyQt6.QtWidgets import QApplication

    from .app import desktop, tasks
    from .app import theme as T
    from .app.controller import AppController
    from .app.main_window import MainWindow
    from .infra.cli import ensure_private_dir, resolve_data_dir, security_dir_for
    from .infra.errors import UserFacingError
    from .infra.monitor import AccountMonitor
    from .infra.secrets import KeyringSecretStore
    from .infra.store import LocalStore

    QApplication.setHighDpiScaleFactorRoundingPolicy(Qt.HighDpiScaleFactorRoundingPolicy.PassThrough)
    try:  # Shared GL contexts are required when QtWebEngine is created after the application.
        QApplication.setAttribute(Qt.ApplicationAttribute.AA_ShareOpenGLContexts)
        import PyQt6.QtWebEngineWidgets  # noqa: F401
        has_webengine = True
    except ImportError:
        has_webengine = False
    app = QApplication(sys.argv[:1])
    app.setApplicationName("AI Token Scrooge")
    app.setDesktopFileName("ai-token-scrooge")
    app.setWindowIcon(T.app_icon())
    app.setStyleSheet(T.STYLESHEET)
    app.setQuitOnLastWindowClosed(False)
    tasks.init()

    chosen, source = resolve_data_dir(args.data_dir)
    try:
        data_dir = ensure_private_dir(chosen)
    except (UserFacingError, OSError) as error:
        print(f"AI Token Scrooge cannot use the data folder {chosen}: {error}", file=sys.stderr)
        return 2
    # --data-dir is an isolated run and never touches the sign-in entry. A SCROOGE_DATA_DIR folder is
    # the user's normal location, so its autostart entry names that folder explicitly.
    autostart_allowed = source != "option"
    autostart_dir = data_dir if source == "environment" else None
    window_holder: list[MainWindow] = []
    instance = desktop.SingleInstance(data_dir, lambda: window_holder and window_holder[0].show_main())
    if not instance.claim():
        return 0

    security_dir = security_dir_for(data_dir)
    store = LocalStore(data_dir / "radar.db")
    secrets = KeyringSecretStore(security_dir, security_dir.name)
    browser = None
    if has_webengine:
        from .app.devin_browser import QtDevinBrowser
        browser = QtDevinBrowser()
    monitor = AccountMonitor(data_dir, security_dir, store, secrets, devin_browser=browser)
    sounds = desktop.Sounds()
    holder: dict = {}
    tray = desktop.Tray(lambda: holder["window"].show_main(), lambda: holder["window"].refresh_by_user(),
                        lambda: holder["window"].exit_app())
    controller = AppController(store, monitor, data_dir, notifier=tray, sounds=sounds)
    window = MainWindow(controller, tray, sounds, compact=args.compact)
    window.autostart_allowed, window.autostart_data_dir = autostart_allowed, autostart_dir
    holder["window"] = window
    window_holder.append(window)

    controller.start(demo=args.demo)
    # Match the Windows default: keep the sign-in entry in sync, except for demo and --data-dir runs.
    if not args.demo and autostart_allowed and controller.settings.LaunchAtSignIn:
        try:
            desktop.set_launch_at_login(True, autostart_dir)
        except OSError:
            pass
    if not (args.background and tray.available):
        window.show()

    signal.signal(signal.SIGINT, lambda *_: window.exit_app())
    heartbeat = QTimer()  # Let Python handle Ctrl+C while Qt's loop runs.
    heartbeat.timeout.connect(lambda: None)
    heartbeat.start(500)
    code = app.exec()
    tasks.shutdown()
    return code


if __name__ == "__main__":
    sys.exit(main())
