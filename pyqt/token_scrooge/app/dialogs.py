"""Connect flows (Codex, GitHub, Claude, Devin), usage details, available resets and alert history.

A connect panel owns a lifetime token. Closing it cancels sign-in, and any credential staged for
an uncommitted connection is disconnected; the sign-in journal keeps a failed cleanup retryable."""
from __future__ import annotations

import webbrowser
from typing import Callable, Optional

from PyQt6.QtCore import QRectF, Qt
from PyQt6.QtGui import QColor, QGuiApplication, QPainter
from PyQt6.QtWidgets import QComboBox, QGridLayout, QHBoxLayout, QLabel, QLineEdit, QVBoxLayout, QWidget

from ..core.labels import format_exact, format_reset, plan_label, provider_display, usage_link
from ..core.model import (CredentialProfileLocation, ProviderAccount, QuotaSnapshot, ReconnectError, UsageUnit,
                          accept_reconnect)
from ..infra.codex import valid_auth_url
from ..infra.errors import CancelToken, safe_message
from ..infra.github import DEFAULT_CLIENT_ID
from ..infra.monitor import new_id
from . import tasks
from . import theme as T
from .panels import Dialog, Panel, PanelHost, message
from .presentation import QuotaRow
from .widgets import ActionButton, Card, hline, label, set_label_color

Connected = Callable[[ProviderAccount, QuotaSnapshot], None]
LOCAL = CredentialProfileLocation.LocalSecurityDirectory
PROVIDERS = [("OpenAI Codex", "OpenAI"), ("GitHub Copilot", "GitHub Copilot"), ("Claude", "Claude"), ("Devin", "Devin")]


def open_url(url: str) -> bool:
    try:
        return bool(webbrowser.open(url, new=2))
    except webbrowser.Error:
        return False


# ---------------------------------------------------------------- provider chooser

class ProviderChoice(ActionButton):
    def __init__(self, title: str, provider: str):
        super().__init__(title, height=56)
        self.provider = provider
        self.setMinimumWidth(240)

    def paintEvent(self, event):
        text = self.text()
        self.setText("")
        super().paintEvent(event)
        self.setText(text)
        p = QPainter(self)
        p.drawPixmap(QRectF(18, 14, 28, 28).toRect(), T.provider_pixmap(self.provider, 28))
        p.setFont(self.font())
        p.setPen(QColor("#FFFFFF" if self.underMouse() else T.TEXT))
        p.drawText(QRectF(60, 0, self.width() - 70, self.height()), Qt.AlignmentFlag.AlignVCenter, text)


def choose_provider(host: PanelHost, chosen: Callable[[str], None]) -> Dialog:
    grid = QWidget()
    layout = QGridLayout(grid)
    layout.setContentsMargins(0, 0, 0, 0)
    layout.setSpacing(10)
    dialog: Optional[Dialog] = None
    for i, (title, provider) in enumerate(PROVIDERS):
        button = ProviderChoice(title, provider)

        def pick(_=False, p=provider):
            host.close_panel(dialog)
            chosen(p)
        button.clicked.connect(pick)
        layout.addWidget(button, i // 2, i % 2)
    dialog = Dialog("Connect Account", "Choose the service to monitor. Scrooge only reads usage and reset times.",
                    (("Cancel", None),), extra=grid)
    host.show_panel(dialog)
    return dialog


# ---------------------------------------------------------------- base connect panel

class ConnectPanel(Panel):
    def __init__(self, title: str, explanation: str, connected: Connected, reconnect: Optional[ProviderAccount]):
        super().__init__(title, max_width=720, max_height=600, body_width=640, close_text="Cancel")
        self.connected, self.reconnect = connected, reconnect
        self.lifetime = CancelToken()
        self.committed = False
        self.busy = False
        self.add(label(explanation, 14, T.TEXT_SECONDARY, wrap=True))
        self.add(label("Account Nickname", 13, T.TEXT_DETAIL))
        self.nickname = QLineEdit(reconnect.Nickname if reconnect else "Personal")
        self.nickname.setPlaceholderText("Account nickname (Personal, Work…)")
        self.nickname.setAccessibleName("Account Nickname")
        self.add(self.nickname)
        self.form = QVBoxLayout()
        self.form.setSpacing(12)
        self.layout_body.addLayout(self.form)
        self.status = label("", 13, T.TEXT_SECONDARY, wrap=True)
        self.status.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        self.connect_button = ActionButton("Connect")
        self.connect_button.clicked.connect(self._connect_clicked)
        self.add(self.status)
        row = QHBoxLayout()
        row.addWidget(self.connect_button)
        row.addStretch(1)
        self.layout_body.addLayout(row)
        self.layout_body.addStretch(1)

    def set_status(self, text: str, problem: bool = False) -> None:
        self.status.setText(text)
        set_label_color(self.status, T.EXPIRY if problem else T.TEXT_SECONDARY)

    def nickname_value(self) -> Optional[str]:
        value = self.nickname.text().strip()
        if not value:
            self.set_status("Enter an account nickname.", True)
            return None
        return value

    def _connect_clicked(self) -> None:
        if self.busy:
            return
        self.connect()

    def connect(self) -> None:
        raise NotImplementedError

    def accept(self, account: ProviderAccount, snapshot: QuotaSnapshot) -> None:
        """Commit on the UI thread. Reconnect keeps the original account identity and settings."""
        if self.lifetime.cancelled:
            return
        try:
            if self.reconnect is not None:
                account = accept_reconnect(self.reconnect, account)
            self.connected(account, snapshot)
        except ReconnectError as error:
            self.set_status(str(error), True)
            self.cleanup_pending(account)
            return
        except Exception:  # noqa: BLE001
            self.set_status("Scrooge could not save this account. Check available space and folder access, then try again.", True)
            self.cleanup_pending(account)
            return
        self.committed = True
        if self.host is not None:
            self.host.close_panel(self)

    def cleanup_pending(self, account: Optional[ProviderAccount]) -> None:
        if account is None or self.committed:
            return
        monitor = self.monitor

        def work():
            monitor.disconnect(account, CancelToken(8))
        tasks.run(work, failed=lambda e: None)

    def before_close(self) -> bool:
        self.lifetime.cancel()
        self.on_cancel()
        return True

    def on_cancel(self) -> None:
        pass

    def run(self, busy_text: str, work: Callable[[], object], done: Callable[[object], None],
            failed: Optional[Callable[[], None]] = None) -> None:
        self.busy = True
        self.connect_button.set_busy(busy_text)

        def finished(result):
            self.busy = False
            try:
                self.connect_button.set_busy(None)
            except RuntimeError:
                return
            if isinstance(result, BaseException):
                if failed:
                    failed()
                if not self.lifetime.cancelled:
                    self.set_status(safe_message(result), True)
                return
            done(result)
        tasks.run(work, done=finished, failed=finished)


# ---------------------------------------------------------------- OpenAI Codex

class CodexConnectPanel(ConnectPanel):
    def __init__(self, monitor, connected: Connected, reconnect: Optional[ProviderAccount] = None):
        self.monitor = monitor
        super().__init__("Connect OpenAI Codex",
                         "Sign in through OpenAI in your browser. Scrooge keeps this account in its own protected Codex "
                         "profile and only reads usage limits.", connected, reconnect)
        self.connect_button.setText("Sign In with OpenAI")
        self.credential_id = new_id()
        self.account_id = reconnect.Id if reconnect else new_id()
        self.connection = None
        self.login_id: Optional[str] = None
        self.started = False
        self.finishing = False

    def connect(self) -> None:
        nickname = self.nickname_value()
        if nickname is None:
            return
        if self.connection is not None:
            self.finish()
            return
        self.started = True
        lifetime = self.lifetime

        def work():
            connection = self.monitor.begin_codex_sign_in(self.credential_id, lifetime)
            try:
                connection.on_notification = self._notification
                login = connection.call("account/login/start", {"type": "chatgpt"}, lifetime)
                url = login.get("authUrl") if isinstance(login, dict) else None
                if not valid_auth_url(url):
                    raise ValueError("OpenAI returned an unexpected sign-in address.")
                lifetime.check()
            except BaseException:
                connection.close()
                raise
            return connection, url, login.get("loginId")

        def opened(result):
            self.connection, url, self.login_id = result
            self.set_status("Complete sign-in in your browser. This window will confirm the account when you finish.")
            self.connect_button.setText("Check Sign-In")
            if not open_url(url):
                self.set_status("Open this address in your browser to sign in:\n" + url)
        self.run("Connecting…", work, opened)

    def _notification(self, root: dict) -> None:
        if root.get("method") == "account/login/completed":
            tasks.post(lambda _: self._completed())

    def _completed(self) -> None:
        if self.lifetime.cancelled:
            return
        self.set_status("Sign-in completed. Checking account…")
        self.finish()

    def finish(self) -> None:
        if self.connection is None or self.finishing:
            return
        self.finishing = True
        nickname = self.nickname.text().strip() or "Personal"
        connection = self.connection

        def work():
            return self.monitor.complete_codex(connection, self.account_id, nickname, self.credential_id, self.lifetime)

        def done(result):
            self.finishing = False
            account, snapshot = result
            self._close_connection()
            self.accept(account, snapshot)

        def failed():
            # The connection stays open so the user can retry Check Sign-In.
            self.finishing = False
        self.run("Checking…", work, done, failed)

    def _close_connection(self) -> None:
        connection, login_id = self.connection, self.login_id
        self.connection = None
        if connection is None:
            return

        def work():
            if login_id:
                try:
                    connection.call("account/login/cancel", {"loginId": login_id}, CancelToken(3))
                except Exception:  # noqa: BLE001
                    pass
            connection.close()
        tasks.run(work, failed=lambda e: None)

    def on_cancel(self) -> None:
        self._close_connection()
        if not self.committed and self.started:
            self.cleanup_pending(ProviderAccount(self.credential_id, "OpenAI", "", "", "", CredentialId=self.credential_id,
                                                 ProfileLocation=LOCAL))


# ---------------------------------------------------------------- GitHub Copilot

class GitHubConnectPanel(ConnectPanel):
    def __init__(self, monitor, connected: Connected, reconnect: Optional[ProviderAccount] = None):
        self.monitor = monitor
        super().__init__("Connect GitHub Copilot", "", connected, reconnect)
        self.explanation = self.layout_body.itemAt(0).widget()
        org_scope = reconnect is not None and reconnect.Scope.startswith("organization:")
        self.use_token = org_scope
        self.mode = ActionButton("Use an Access Token Instead")
        self.mode.clicked.connect(self._toggle_mode)
        mode_row = QHBoxLayout()
        mode_row.addWidget(self.mode)
        mode_row.addStretch(1)
        self.form.addLayout(mode_row)

        self.advanced = QWidget()
        adv = QVBoxLayout(self.advanced)
        adv.setContentsMargins(0, 0, 0, 0)
        adv.setSpacing(8)
        self.token = QLineEdit()
        self.token.setEchoMode(QLineEdit.EchoMode.Password)
        self.token.setPlaceholderText("Fine-grained access token")
        self.token.setAccessibleName("GitHub Access Token")
        self.organization = QLineEdit(reconnect.Scope[13:] if org_scope else "")
        self.organization.setPlaceholderText("Organization (blank for personal billing)")
        self.organization.setReadOnly(reconnect is not None)
        self.allowance = QLineEdit("" if reconnect is None or reconnect.Allowance is None else f"{reconnect.Allowance:g}")
        self.allowance.setPlaceholderText("Monthly allowance (optional)")
        self.unit = QComboBox()
        self.unit.addItems(["Credits", "Premium Requests"])
        if reconnect is not None:
            self.unit.setCurrentIndex(1 if reconnect.Unit == UsageUnit.Requests else 0)
            self.unit.setEnabled(False)
        for w in (self.token, self.organization, self.allowance, self.unit):
            adv.addWidget(w)
        self.form.addWidget(self.advanced)

        self.prompt = Card(13)
        pl = QVBoxLayout(self.prompt)
        pl.setContentsMargins(20, 18, 20, 18)
        pl.setSpacing(12)
        pl.addWidget(label("Enter this code on GitHub, then approve AI Reset Radar (the registered sign-in app for AI Token "
                           "Scrooge).", 13, T.TEXT_SECONDARY, wrap=True, align=Qt.AlignmentFlag.AlignCenter))
        self.code = label("", 28, T.ACCENT, True, align=Qt.AlignmentFlag.AlignCenter)
        self.code.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
        self.code.setAccessibleDescription("GitHub sign-in code. Enter this code only on github.com.")
        pl.addWidget(self.code)
        buttons = QHBoxLayout()
        buttons.addStretch(1)
        copy = ActionButton("Copy Code", "copy")
        copy.clicked.connect(self._copy)
        github = ActionButton("Open GitHub", "link")
        github.clicked.connect(self._open_github)
        buttons.addWidget(copy)
        buttons.addWidget(github)
        buttons.addStretch(1)
        pl.addLayout(buttons)
        self.layout_body.insertWidget(self.layout_body.count() - 1, self.prompt)
        self.prompt.hide()
        self.challenge = None
        self._apply_mode()

    def _toggle_mode(self) -> None:
        if self.busy:
            return
        self.use_token = not self.use_token
        self._apply_mode()

    def _apply_mode(self) -> None:
        self.advanced.setVisible(self.use_token)
        self.mode.setText("Use Browser Sign-In Instead" if self.use_token else "Use an Access Token Instead")
        self.mode.updateGeometry()
        self.connect_button.setText("Connect Account" if self.use_token else "Sign In with GitHub")
        self.connect_button.updateGeometry()
        self.explanation.setText(
            "Advanced connection: use a fine-grained token with Plan read permission for personal billing. Organization billing "
            "requires authorized administration access. Your token stays protected in this computer's keychain."
            if self.use_token else
            "Sign in through GitHub in your browser. Scrooge reads your personal Copilot usage and protects this account's "
            "sign-in in this computer's keychain.")
        self.set_status("")

    def _copy(self) -> None:
        if not self.code.text():
            return
        QGuiApplication.clipboard().setText(self.code.text())
        self.set_status("Code copied. Paste it into GitHub to continue.")

    def _open_github(self) -> None:
        uri = self.challenge.verification_uri if self.challenge else "https://github.com/login/device"
        if not open_url(uri):
            self.set_status("Open github.com/login/device in your browser and enter the code shown here.")

    def connect(self) -> None:
        nickname = self.nickname_value()
        if nickname is None:
            return
        allowance = None
        unit = UsageUnit.Requests if self.unit.currentIndex() == 1 else UsageUnit.Credits
        if self.use_token:
            text = self.allowance.text().strip()
            if text:
                try:
                    allowance = float(text)
                except ValueError:
                    allowance = -1
                if allowance <= 0:
                    self.set_status("Allowance must be a positive number, or blank.", True)
                    return
            if not self.token.text().strip():
                self.set_status("Enter the access token in this protected field.", True)
                return
        lifetime = self.lifetime
        token_value = self.token.text().strip() if self.use_token else None
        organization = self.organization.text().strip() if self.use_token else None
        if self.use_token:
            self.token.clear()

        def connect_with(app_tokens):
            return self.monitor.connect_github(nickname, token_value, app_tokens, allowance, unit, organization, lifetime,
                                               self.reconnect)

        if self.use_token:
            self.set_status("Checking GitHub identity and billing access…")
            self.run("Connecting…", lambda: connect_with(None), lambda r: self.accept(*r))
            return

        self.set_status("Opening GitHub sign-in…")

        def begin():
            return self.monitor.github_flow.begin(DEFAULT_CLIENT_ID)

        def started(challenge):
            self.challenge = challenge
            self.code.setText(challenge.user_code)
            self.prompt.show()
            self._open_github()
            self.set_status("Complete sign-in in your browser. Scrooge will connect when you approve access.")

            def wait():
                tokens = self.monitor.github_flow.wait(challenge, lifetime)
                tasks.post(lambda _: self.set_status("Checking GitHub identity and billing access…"))
                return connect_with(tokens)

            def finished(result):
                self.code.setText("")
                self.prompt.hide()
                self.challenge = None
                self.accept(*result)
            self.run("Waiting for GitHub…", wait, finished)
        self.run("Connecting…", begin, started)

    def on_cancel(self) -> None:
        self.token.clear()
        self.code.setText("")
        self.challenge = None


# ---------------------------------------------------------------- Claude and Devin

class BrowserConnectPanel(ConnectPanel):
    def __init__(self, provider: str, monitor, connected: Connected, reconnect: Optional[ProviderAccount] = None):
        self.monitor, self.provider = monitor, provider
        devin = provider == "Devin"
        super().__init__(f"Connect {provider}",
                         "Paste your usual Devin Enterprise workspace URL and sign in. Scrooge reads your personal monthly ACUs "
                         "and reset time from My Analytics." if devin else
                         "Sign in through Claude in your browser. Scrooge uses a separate Claude Code profile to read your "
                         "session and weekly limits. Requires Claude Code 2.1.310 or later.", connected, reconnect)
        self.workspace = QLineEdit(f"https://{reconnect.Scope}" if devin and reconnect else "")
        self.workspace.setPlaceholderText("Company Devin Workspace URL")
        self.workspace.setReadOnly(reconnect is not None)
        self.workspace.setAccessibleName("Company Devin Workspace URL")
        self.workspace.setVisible(devin)
        self.form.addWidget(self.workspace)
        self.connect_button.setText("Sign In with Devin" if devin else "Sign In with Claude")
        if devin and monitor.devin.browser is None:
            self.set_status("Devin needs the PyQt6-WebEngine package. Install it, then restart Scrooge.", True)

    def connect(self) -> None:
        nickname = self.nickname_value()
        if nickname is None:
            return
        credential = new_id()
        account_id = self.reconnect.Id if self.reconnect else new_id()
        token = self.lifetime.child(600 if self.provider == "Devin" else 300)
        url = self.workspace.text()
        self.set_status(f"Complete {self.provider} sign-in in your browser. Scrooge will check the account and usage when you finish.")
        pending = ProviderAccount(credential, self.provider, "", "", "", CredentialId=credential, ProfileLocation=LOCAL)

        def work():
            try:
                if self.provider == "Devin":
                    return self.monitor.devin.connect(account_id, credential, nickname, url, token)
                return self.monitor.claude.connect(account_id, credential, nickname, token)
            except BaseException:
                try:
                    self.monitor.disconnect(pending, CancelToken(8))
                except Exception:  # noqa: BLE001 - journal keeps it retryable
                    pass
                raise

        def done(result):
            account, snapshot = result
            self.accept(account, snapshot)
            if not self.committed:
                self.cleanup_pending(account)
        self.run("Waiting for Sign-In…", work, done)


# ---------------------------------------------------------------- details

def show_details(host: PanelHost, row: QuotaRow, zone, demo: bool) -> None:
    account, snapshot, window = row.Account, row.Snapshot, row.Window
    details = (f"{row.window_title}\n{row.used_text} · {row.left_text}\nResets: {format_exact(window.ResetsAt, zone)}\n\n"
               f"Account: {account.Login or account.Subject}\nScope: {account.Scope}\n"
               f"Last successful fetch: {format_exact(snapshot.ObservedAt, zone)}\n"
               f"Provider data as of: {format_exact(snapshot.ProviderDataAt, zone)}\n\n{row.source_note}").strip()
    title = f"{provider_display(account.Provider)} / {row.account_label}"
    link = None if demo else usage_link(account)
    if link is None:
        message(host, title, details)
        return

    def open_provider():
        if not open_url(link):
            message(host, "Provider Page Unavailable", "Open your provider's usage page in your browser, and check its connection status.")
    message(host, title, details + "\n\nProvider pages use your browser's sign-in. Check its account and workspace before comparing.",
            buttons=(("Open Provider Usage", open_provider), ("Close", None)))


def show_reserve(host: PanelHost, state, zone, account_id: Optional[str], now) -> None:
    blocks = []
    for account in state.accounts:
        if (account_id is None and not account.Enabled) or (account_id is not None and account.Id != account_id):
            continue
        snapshot = state.snapshots.get(account.Id)
        reserve = snapshot.Reserve if snapshot else None
        if reserve is None:
            continue
        count = "Unknown" if reserve.AvailableCount is None else str(reserve.AvailableCount)
        lines = [f"{provider_display(account.Provider)} / {account.Nickname} · {plan_label(account.Provider, snapshot.PlanType)}: "
                 f"{count} reported available",
                 f"Snapshot fetched {format_reset(snapshot.ObservedAt, zone)} · "
                 f"{snapshot.Health.name if account.Enabled else 'Monitoring paused'}"]
        if reserve.has_partial_details:
            lines.append("Provider supplied only partial credit details.")
        if reserve.Credits:
            for credit in reserve.Credits:
                status = "Expired locally — checking provider" if credit.ExpiresAt and credit.ExpiresAt <= now else credit.Status
                lines.append(f"• {format_reset(credit.ExpiresAt, zone)} · {status}")
        else:
            lines.append("Credit details unavailable")
        blocks.append("\n".join(lines))
    message(host, "Available Resets", "\n\n".join(blocks) or "Your provider has not reported earned reset availability.")


class AlertHistoryPanel(Panel):
    def __init__(self, controller, open_account: Callable[[str], None]):
        super().__init__("Alert History", max_width=820, body_width=760)
        zone = controller.zone
        if controller.problem:
            self.add(label(controller.problem, 13, T.EXPIRY, wrap=True))
        alerts = sorted(controller.alert_history, key=lambda a: a.CreatedAt, reverse=True)
        if not alerts:
            self.add(label("No alerts yet. Low capacity and expiring resets will appear here.", 14, T.TEXT_SECONDARY, wrap=True))
        for alert in alerts:
            card = Card(13)
            layout = QVBoxLayout(card)
            layout.setContentsMargins(16, 12, 16, 12)
            layout.setSpacing(4)
            layout.addWidget(label(f"{format_reset(alert.CreatedAt, zone)} · {alert.Title}", 14, T.TEXT, True, wrap=True))
            layout.addWidget(label(alert.Detail, 13, T.TEXT_SECONDARY, wrap=True))
            layout.addWidget(label("Sent as a Desktop Notification" if alert.Delivered else "Recorded in History", 12, T.TEXT_TERTIARY))
            card.setCursor(Qt.CursorShape.PointingHandCursor)
            card.mouseReleaseEvent = lambda e, a=alert.AccountId: (self.request_close(), open_account(a))
            self.add(card)
        self.layout_body.addStretch(1)
