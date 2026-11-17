"""Explicit per-provider connect/refresh/disconnect routes (port of AccountMonitor).
Unknown providers are rejected; they never fall through to another provider's route."""
from __future__ import annotations

import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional

from ..core.model import (CredentialProfileLocation, ProviderAccount, QuotaSnapshot, SourceHealth, UsageUnit,
                          accept_reconnect, replace)
from .claude import ClaudeClient
from .cli import ProfilePaths
from .codex import CodexConnection, find_codex, map_codex
from .devin import DevinBrowser, DevinClient
from .errors import CancelToken, ProviderAccessError, UserFacingError
from .github import GitHubAppTokens, GitHubCredentialManager, GitHubDeviceFlow, GitHubUsageClient
from .store import LocalStore

LOCAL = CredentialProfileLocation.LocalSecurityDirectory


def new_id() -> str:
    return uuid.uuid4().hex


class AccountMonitor:
    def __init__(self, data_dir: Path, security_dir: Path, store: LocalStore, secrets,
                 github_usage: Optional[GitHubUsageClient] = None, github_flow: Optional[GitHubDeviceFlow] = None,
                 devin_browser: Optional[DevinBrowser] = None, clock=None):
        self.clock = clock or (lambda: datetime.now(timezone.utc))
        self.profiles = ProfilePaths(data_dir, security_dir)
        self.store = store
        self.secrets = secrets
        self.github = github_usage or GitHubUsageClient(clock=self.clock)
        self.github_flow = github_flow or GitHubDeviceFlow(clock=self.clock)
        self.github_credentials = GitHubCredentialManager(secrets, self.github, self.github_flow, self.clock)
        self.claude = ClaudeClient(self.profiles, store, self.clock)
        self.devin = DevinClient(self.profiles, secrets, devin_browser, store, self.clock)

    # ------------------------------------------------------------------ Codex
    def open_codex(self, credential_id: str, location: CredentialProfileLocation, token: CancelToken) -> CodexConnection:
        profile = self.profiles.ensure("OpenAI", credential_id, location)
        return CodexConnection.start(find_codex(), profile, token)

    def begin_codex_sign_in(self, credential_id: str, token: CancelToken) -> CodexConnection:
        find_codex()  # Fail before journaling when the CLI is missing.
        self.store.stage_credential("OpenAI", credential_id, LOCAL)
        return self.open_codex(credential_id, LOCAL, token)

    def complete_codex(self, connection: CodexConnection, account_id: str, nickname: str, credential_id: str,
                       token: CancelToken) -> tuple[ProviderAccount, QuotaSnapshot]:
        identity = connection.call("account/read", {"refreshToken": False}, token)
        account_data = identity.get("account") if isinstance(identity, dict) else None
        email = account_data.get("email") if isinstance(account_data, dict) else None
        if not isinstance(email, str):
            raise ProviderAccessError("Complete sign-in with a ChatGPT account in your browser, then try again.",
                                      SourceHealth.AuthenticationRequired)
        data = connection.call("account/rateLimits/read", None, token)
        scope = data.get("accountId") if isinstance(data, dict) and isinstance(data.get("accountId"), str) else "unresolved"
        account = ProviderAccount(account_id, "OpenAI", nickname, email, scope, CredentialId=credential_id, ProfileLocation=LOCAL)
        plan = account_data.get("planType") if isinstance(account_data.get("planType"), str) else None
        return account, replace(map_codex(account, data, self.clock()), PlanType=plan)

    # ------------------------------------------------------------------ GitHub
    def connect_github(self, nickname: str, token_value: Optional[str], app_tokens: Optional[GitHubAppTokens],
                       allowance: Optional[float], unit: UsageUnit, organization: Optional[str], token: CancelToken,
                       reconnect: Optional[ProviderAccount] = None) -> tuple[ProviderAccount, QuotaSnapshot]:
        access = app_tokens.AccessToken if app_tokens else token_value
        if not access:
            raise UserFacingError("Enter the access token in this protected field.")
        if app_tokens and reconnect is not None and reconnect.Scope != "personal":
            raise UserFacingError("Organization billing uses the advanced token connection.")
        subject, login = self.github.identify(access)
        if reconnect is not None and reconnect.Subject != subject:
            raise ProviderAccessError("Sign in to the same GitHub account to reconnect. Add a separate connection for a different account.",
                                      SourceHealth.AuthenticationRequired)
        scope = "organization:" + organization.strip().lower() if organization and organization.strip() else "personal"
        account = ProviderAccount(reconnect.Id if reconnect else new_id(), "GitHub Copilot", nickname, subject, scope,
                                  Allowance=allowance, Unit=unit, Login=login, CredentialId=new_id())
        if reconnect is not None:
            account = accept_reconnect(reconnect, account)
        try:
            snapshot = self.github.read(account, access)
        except ProviderAccessError as error:
            snapshot = QuotaSnapshot(account.Id, account.Subject, account.Scope, self.clock(), (), None, error.health, str(error))
        token.check()
        self.store.stage_credential("GitHub Copilot", account.credential, CredentialProfileLocation.LegacyDataDirectory)
        try:
            if app_tokens is None:
                self.github_credentials.save_token(account.credential, access)
            else:
                self.github_credentials.save_app(account.credential, subject, app_tokens)
            token.check()
        except BaseException:
            try:
                self.disconnect(account, CancelToken(8))
            except Exception:  # noqa: BLE001 - the journal retains failed cleanup
                pass
            raise
        return account, snapshot

    # ------------------------------------------------------------------ routes
    def refresh(self, account: ProviderAccount, token: CancelToken) -> QuotaSnapshot:
        if account.Provider == "Devin":
            return self.devin.refresh(account, token)
        if account.Provider == "Claude":
            return self.claude.refresh(account, token)
        if account.Provider == "GitHub Copilot":
            return self.github.read(account, self.github_credentials.read_access_token(account, token))
        if account.Provider != "OpenAI":
            raise UserFacingError("Unknown credential provider.")
        with self.open_codex(account.credential, account.ProfileLocation, token) as connection:
            identity = connection.call("account/read", {"refreshToken": False}, token)
            data = identity.get("account") if isinstance(identity, dict) else None
            if not isinstance(data, dict) or data.get("email") != account.Subject:
                raise ProviderAccessError("Codex is signed out or the profile identity changed. Reconnect this account.",
                                          SourceHealth.AuthenticationRequired)
            plan = data.get("planType") if isinstance(data.get("planType"), str) else None
            return replace(map_codex(account, connection.call("account/rateLimits/read", None, token), self.clock()), PlanType=plan)

    def disconnect(self, account: ProviderAccount, token: CancelToken) -> None:
        if account.Provider == "Devin":
            self.devin.disconnect(account, token)
            return
        if account.Provider == "Claude":
            self.claude.disconnect(account, token)
            return
        if account.Provider == "GitHub Copilot":
            self.github_credentials.remove(account.credential)
        elif account.Provider == "OpenAI":
            profile = self.profiles.path_for("OpenAI", account.credential, account.ProfileLocation)
            if profile.is_dir():
                with self.open_codex(account.credential, account.ProfileLocation, token) as connection:
                    connection.call("account/logout", None, token)
            token.check()
            self.profiles.delete("OpenAI", account.credential, account.ProfileLocation)
        else:
            raise UserFacingError("Unknown credential provider.")
        # Removal completed; a caller deadline must not turn success into another cleanup cycle.
        self.store.complete_credential_cleanup(account.credential)
