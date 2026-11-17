"""Devin Enterprise reader on QtWebEngine (port of the Windows WebView2 DevinBrowser).

Each connection gets its own persistent browser profile inside the Scrooge security directory.
The view allows HTTPS navigation only and blocks pop-ups, downloads and permission prompts.
Only the profile user ID and the My Analytics meter text are read from the page."""
from __future__ import annotations

import json
import re
import threading
import time
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Callable, Optional
from urllib.parse import urlsplit

from PyQt6.QtCore import QTimer, QUrl
from PyQt6.QtWebEngineCore import QWebEnginePage, QWebEngineProfile, QWebEngineSettings
from PyQt6.QtWebEngineWidgets import QWebEngineView

from ..core.model import SourceHealth
from ..infra.devin import DevinReading, parse_usage
from ..infra.errors import Cancelled, CancelToken, ProviderAccessError
from . import tasks
from . import theme as T

IDENTITY_SCRIPT = "document.querySelector('#profile-user-id p')?.innerText?.trim() || null"
USAGE_SCRIPT = r"""
(() => {
    const text = document.querySelector('#main-content')?.innerText ?? '';
    const amount = '([0-9][0-9,.]*)';
    const numbers = text.match(new RegExp(amount + '\\s*\\/\\s*' + amount + '\\s*\\(\\s*[0-9.]+\\s*%\\s*\\)\\s*ACUs consumed since', 'i'));
    const monthly = text.match(/Monthly ACU limit usage\s+([0-9][0-9,.]*)\s*\/\s*([0-9][0-9,.]*)/i);
    const meter = numbers ?? monthly;
    const used = text.match(/Monthly ACUs consumed\s+([0-9][0-9,.]*)/i);
    if (!meter && !used) return null;
    const reset = text.match(/Resets\s+(\d{1,2}\/\d{1,2})\s+at\s+(\d{1,2}:\d{2}\s*[AP]M)/i);
    return [meter?.[1] ?? used[1], meter?.[2] ?? '', reset ? reset[1] + ' ' + reset[2] : '',
        Intl.DateTimeFormat().resolvedOptions().timeZone ?? ''];
})()
"""
SUBJECT = re.compile(r"[A-Za-z0-9:|_-]{1,160}")


class _GuardedPage(QWebEnginePage):
    def acceptNavigationRequest(self, url: QUrl, nav_type, is_main_frame: bool) -> bool:
        return url.scheme() == "https"

    def createWindow(self, window_type):
        return None  # No pop-ups or new windows.


class _Session:
    """Runs on the UI thread. Mirrors ReadOnUiAsync: identity, analytics, identity again."""

    def __init__(self, origin: str, profile_dir: str, interactive: bool, finish: Callable[[Any], None]):
        self.origin, self.interactive, self._finish = origin, interactive, finish
        self.done = False
        storage = Path(profile_dir) / "web"
        storage.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.profile = QWebEngineProfile(f"scrooge-devin-{Path(profile_dir).name}")
        self.profile.setPersistentStoragePath(str(storage))
        self.profile.setCachePath(str(storage / "cache"))
        self.profile.setPersistentCookiesPolicy(QWebEngineProfile.PersistentCookiesPolicy.ForcePersistentCookies)
        self.profile.setHttpCacheType(QWebEngineProfile.HttpCacheType.DiskHttpCache)
        self.profile.downloadRequested.connect(lambda item: item.cancel())
        self.page = _GuardedPage(self.profile)
        settings = self.page.settings()
        for attribute, value in ((QWebEngineSettings.WebAttribute.JavascriptCanOpenWindows, False),
                                 (QWebEngineSettings.WebAttribute.LocalContentCanAccessRemoteUrls, False),
                                 (QWebEngineSettings.WebAttribute.LocalContentCanAccessFileUrls, False),
                                 (QWebEngineSettings.WebAttribute.PluginsEnabled, False),
                                 (QWebEngineSettings.WebAttribute.JavascriptCanAccessClipboard, False)):
            settings.setAttribute(attribute, value)
        if hasattr(self.page, "permissionRequested"):  # Qt 6.8+
            self.page.permissionRequested.connect(lambda permission: permission.deny())
        else:
            self.page.featurePermissionRequested.connect(
                lambda origin, feature: self.page.setFeaturePermission(origin, feature, QWebEnginePage.PermissionPolicy.PermissionDeniedByUser))
        self.view = QWebEngineView()
        self.view.setPage(self.page)
        self.view.resize(1000, 760)
        self.view.setWindowTitle("Sign In with Devin")
        self.view.setWindowIcon(T.app_icon())
        self.view.closeEvent = self._closed
        self.page.urlChanged.connect(self._url_changed)
        self.page.loadStarted.connect(self._load_started)
        self.page.loadFinished.connect(self._load_finished)
        self.loading = True
        self.loaded_ok: Optional[bool] = None
        self.timer = QTimer()
        self.timer.timeout.connect(self._poll)
        self.target: Optional[QUrl] = None
        self.subject: Optional[str] = None
        self.step = "identity"
        if interactive:
            self.view.show()
            self.view.raise_()
            self.view.activateWindow()
        self._navigate("/settings/preferences", IDENTITY_SCRIPT, interactive)

    # ----------------------------------------------------------- lifecycle
    def _closed(self, event):
        event.accept()
        self.fail(Cancelled("Devin sign-in was closed."))

    def fail(self, error: BaseException) -> None:
        self._complete(error)

    def _complete(self, value: Any) -> None:
        if self.done:
            return
        self.done = True
        self.timer.stop()
        self.view.closeEvent = lambda e: e.accept()
        self.view.hide()
        self.view.setPage(None)
        self.page.deleteLater()
        self.view.deleteLater()
        self.profile.deleteLater()
        self._finish(value)

    def _url_changed(self, url: QUrl) -> None:
        if self.interactive and not self.done:
            self.view.setWindowTitle(f"Sign In with Devin — {url.scheme()}://{url.host()}")

    def _load_started(self) -> None:
        self.loading, self.loaded_ok = True, None

    def _load_finished(self, ok: bool) -> None:
        self.loading, self.loaded_ok = False, ok

    # ----------------------------------------------------------- page reads
    def _navigate(self, path: str, expression: str, allow_sign_in: bool) -> None:
        self.target = QUrl(self.origin + path)
        script_target = json.dumps(self.origin)
        self.script = (f"location.origin === {script_target} && location.pathname === {json.dumps(path)} "
                       f"? ({expression}) : null")
        self.allow_sign_in = allow_sign_in
        self.arrived: Optional[float] = None
        self.retried_landing = False
        self.evaluating = False
        self.loading, self.loaded_ok = True, None
        self.page.load(self.target)
        self.timer.start(350)

    def _poll(self) -> None:
        if self.done or self.evaluating or self.loading or self.loaded_ok is None:
            return
        if not self.loaded_ok:
            if self.allow_sign_in:
                return  # Identity providers may cancel intermediate loads during sign-in.
            self.fail(ProviderAccessError("The Devin page could not load. Check your connection and try again.", SourceHealth.Offline))
            return
        page = self.page.url()
        target = self.target
        if page.scheme() != "https" or page.host() != target.host() or page.path() != target.path():
            self.arrived = None
            if page.host() == target.host() and page.path().startswith("/org/") and self.allow_sign_in and not self.retried_landing:
                self.retried_landing = True
                self.page.load(target)
                return
            if self.allow_sign_in:
                return
            login = page.host() != target.host() or page.path().startswith("/auth/")
            self.fail(ProviderAccessError("Your Devin sign-in expired. Use Sign In Again." if login else
                                          "Your account could not open Devin My Analytics.",
                                          SourceHealth.AuthenticationRequired if login else SourceHealth.PermissionRequired))
            return
        if self.arrived is None:
            self.arrived = time.monotonic()
        self.evaluating = True
        self.page.runJavaScript(self.script, self._evaluated)

    def _evaluated(self, value: Any) -> None:
        self.evaluating = False
        if self.done:
            return
        elapsed = time.monotonic() - (self.arrived or time.monotonic())
        if self.step in ("identity", "verify"):
            if isinstance(value, str) and value:
                if not SUBJECT.fullmatch(value):
                    self.fail(ProviderAccessError("Devin did not supply a recognizable user ID.", SourceHealth.Unsupported))
                    return
                self._identity(value)
                return
        elif isinstance(value, list) and len(value) == 4 and all(isinstance(v, str) and len(v) <= 200 for v in value):
            if (value[1] and value[2]) or elapsed > 12:
                self.timer.stop()
                self.fields = value
                self.step = "verify"
                self._navigate("/settings/preferences", IDENTITY_SCRIPT, False)
                return
        if elapsed > 14:
            self.fail(ProviderAccessError("Devin did not show the expected account or usage information. Check My Analytics "
                                          "in your workspace.", SourceHealth.Unsupported))

    def _identity(self, subject: str) -> None:
        self.timer.stop()
        if self.step == "identity":
            self.subject = subject
            if self.interactive:
                self.view.hide()
            self.step = "usage"
            self._navigate("/settings/my-analytics", USAGE_SCRIPT, False)
            return
        if subject != self.subject:
            self.fail(ProviderAccessError("The Devin account changed during the usage check.", SourceHealth.AuthenticationRequired))
            return
        try:
            self._complete(parse_usage(self.subject, self.fields, datetime.now(timezone.utc)))
        except BaseException as error:  # noqa: BLE001
            self._complete(error)


class QtDevinBrowser:
    """DevinBrowser protocol implementation. `read` blocks a worker thread while the UI thread
    drives the page; cancellation closes the window."""

    def read(self, origin: str, profile: str, interactive: bool, token: CancelToken) -> DevinReading:
        parts = urlsplit(origin)
        if parts.scheme != "https" or not parts.hostname:
            raise ProviderAccessError("Enter your company's Devin Enterprise workspace URL.", SourceHealth.Unsupported)
        finished = threading.Event()
        box: dict[str, Any] = {}
        sessions: list[_Session] = []

        def complete(value: Any) -> None:
            box["value"] = value
            finished.set()

        def start(_=None) -> None:
            if token.cancelled:
                complete(Cancelled("Devin read cancelled."))
                return
            try:
                sessions.append(_Session(f"https://{parts.hostname}", profile, interactive, complete))
            except BaseException as error:  # noqa: BLE001
                complete(error)

        tasks.post(start)
        while not finished.wait(0.2):
            if token.cancelled:
                tasks.post(lambda _: sessions and sessions[0].fail(Cancelled("Devin read cancelled.")))
                finished.wait(5)
                raise Cancelled("Devin read cancelled.")
        value = box.get("value")
        if isinstance(value, BaseException):
            raise value
        return value
