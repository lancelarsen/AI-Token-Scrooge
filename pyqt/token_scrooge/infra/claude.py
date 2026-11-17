"""Claude through the installed Claude Code CLI with an isolated CLAUDE_CONFIG_DIR (port of ClaudeClient).

Usage comes from the CLI's `/usage` command. Monitoring never falls back to asking a model to
estimate usage, and never sends a prompt.
"""
from __future__ import annotations

import json
import os
import re
import stat
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Optional
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

from ..core.model import (AllowanceProvenance, CredentialProfileLocation, ProviderAccount, QuotaSnapshot, QuotaWindow,
                          SourceHealth, UsageUnit)
from .cli import ProfilePaths, first_line_version, locate, run_bounded, sanitized_environment
from .errors import CancelToken, ProviderAccessError, UserFacingError

MIN_VERSION = (2, 1, 310)
LOCATION = CredentialProfileLocation.LocalSecurityDirectory


def find_claude() -> str:
    found = locate("claude", "SCROOGE_CLAUDE")
    if not found:
        raise UserFacingError("Install Claude Code to connect a Claude account.")
    return found


class ClaudeClient:
    def __init__(self, profiles: ProfilePaths, journal=None, clock=None):
        self.profiles = profiles
        self.journal = journal
        self.clock = clock or (lambda: datetime.now(timezone.utc))

    def _restrict(self, profile: Path) -> None:
        credentials = profile / ".credentials.json"
        try:
            info = credentials.lstat()
        except FileNotFoundError:
            return
        if stat.S_ISLNK(info.st_mode):
            raise UserFacingError("Claude's credential file is redirected. Sign in again.")
        if stat.S_IMODE(info.st_mode) != 0o600:
            os.chmod(credentials, 0o600)

    def _run(self, profile: Path, token: CancelToken, *args: str) -> str:
        profile.mkdir(parents=True, exist_ok=True, mode=0o700)
        self._restrict(profile)
        env = sanitized_environment({"CLAUDE_CONFIG_DIR": str(profile.resolve())})
        timeout = 600.0 if args == ("auth", "login") else 60.0
        try:
            result = run_bounded([find_claude(), *args], env, str(profile.resolve()), timeout, token)
        finally:
            # Refresh/login can replace the file. Recheck permissions without changing its format.
            self._restrict(profile)
        if result.exit_code != 0:
            raise ProviderAccessError("Claude sign-in could not be confirmed. Reconnect this account.",
                                      SourceHealth.AuthenticationRequired)
        return result.output

    def _identity(self, profile: Path, token: CancelToken) -> tuple[str, str, str]:
        try:
            root = json.loads(self._run(profile, token, "auth", "status"))
        except json.JSONDecodeError:
            raise ProviderAccessError("Sign in with your Claude subscription account.", SourceHealth.AuthenticationRequired) from None
        if not isinstance(root, dict):
            root = {}
        email, org = root.get("email"), root.get("orgId")
        if (root.get("loggedIn") is not True or root.get("authMethod") != "claude.ai"
                or not isinstance(email, str) or not email.strip() or not isinstance(org, str) or not org.strip()):
            raise ProviderAccessError("Sign in with your Claude subscription account.", SourceHealth.AuthenticationRequired)
        plan = root.get("subscriptionType")
        return email.lower(), org, plan if isinstance(plan, str) else "unknown"

    def connect(self, account_id: str, credential_id: str, nickname: str, token: CancelToken) -> tuple[ProviderAccount, QuotaSnapshot]:
        if self.journal is not None:
            self.journal.stage_credential("Claude", credential_id, LOCATION)
        profile = self.profiles.ensure("Claude", credential_id, LOCATION)
        self._run(profile, token, "auth", "login")
        subject, scope, plan = self._identity(profile, token)
        account = ProviderAccount(account_id, "Claude", nickname, subject, scope, Login=subject, CredentialId=credential_id,
                                  ProfileLocation=LOCATION)
        return account, self._usage(account, profile, plan, token)

    def refresh(self, account: ProviderAccount, token: CancelToken) -> QuotaSnapshot:
        profile = self.profiles.path_for("Claude", account.credential, account.ProfileLocation)
        if not profile.is_dir():
            raise ProviderAccessError("Sign in to Claude again.", SourceHealth.AuthenticationRequired)
        subject, scope, plan = self._identity(profile, token)
        if subject != account.Subject or scope != account.Scope:
            raise ProviderAccessError("Claude profile identity changed. Reconnect this account.", SourceHealth.AuthenticationRequired)
        return self._usage(account, profile, plan, token)

    def disconnect(self, account: ProviderAccount, token: CancelToken) -> None:
        profile = self.profiles.path_for("Claude", account.credential, account.ProfileLocation)
        if profile.is_dir():
            try:
                self._run(profile, token, "auth", "logout")
            except ProviderAccessError:
                pass  # Already signed out; local deletion below still completes cleanup.
        token.check()
        self.profiles.delete("Claude", account.credential, account.ProfileLocation)
        if self.journal is not None:
            self.journal.complete_credential_cleanup(account.credential)

    def _usage(self, account: ProviderAccount, profile: Path, plan: str, token: CancelToken) -> QuotaSnapshot:
        version = first_line_version(self._run(profile, token, "--version"))
        if version is None or version < MIN_VERSION:
            raise ProviderAccessError("Update Claude Code to version 2.1.310 or later to read usage in Scrooge.", SourceHealth.Unsupported)
        now = self.clock()
        windows = parse_usage(self._run(profile, token, "/usage"), now)
        if not windows:
            raise ProviderAccessError("Claude Code did not return readable usage limits. Check Claude's Usage page.", SourceHealth.Unsupported)
        return QuotaSnapshot(account.Id, account.Subject, account.Scope, now, tuple(windows), None,
                             Message="Read from Claude Code. Reset times are shown to the nearest minute; reset credits are not supplied by this source.",
                             PlanType=plan)


_SECTIONS = (("Current session", "session", "Current session", 300),
             ("Current week (all models)", "weekly", "Weekly limit", 10080))
_MONTHS = {m: i for i, m in enumerate(("jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"), 1)}


def parse_usage(screen: str, now: datetime) -> list[QuotaWindow]:
    result = []
    for label, window_id, name, duration in _SECTIONS:
        start = screen.find(label)
        if start < 0:
            continue
        section = screen[start + len(label):]
        end = re.search(r"\r?\n\s*(?:Current |Extra usage|Esc to cancel)", section)
        if end:
            section = section[:end.start()]
        match = re.search(r"(?<![\w.+-])(?P<used>\d+(?:\.\d+)?)% used", section)
        if not match:
            continue
        used = float(match.group("used"))
        if not 0 <= used <= 100:
            continue
        reset = re.search(r"Resets (?P<when>[^\r\n(]+)\s*\((?P<zone>[^)]+)\)", section, re.IGNORECASE)
        at = _parse_reset(reset.group("when").strip(), reset.group("zone"), now, duration) if reset else None
        result.append(QuotaWindow(window_id, name, UsageUnit.Percent, used, 100, at, duration, AllowanceProvenance.Provider))
    return result


def _parse_reset(value: str, zone_id: str, now: datetime, duration: int) -> Optional[datetime]:
    try:
        zone = ZoneInfo(zone_id.strip())
    except (ZoneInfoNotFoundError, ValueError):
        return None
    match = re.fullmatch(r"(?:(?P<mon>[A-Za-z]{3})\s+(?P<day>\d{1,2}),\s*)?(?P<h>\d{1,2})(?::(?P<m>\d{2}))?\s*(?P<ap>[ap]m)",
                         value.strip(), re.IGNORECASE)
    if not match:
        return None
    hour = int(match.group("h"))
    minute = int(match.group("m") or 0)
    if not 1 <= hour <= 12 or minute > 59:
        return None
    hour = hour % 12 + (12 if match.group("ap").lower() == "pm" else 0)
    local_now = now.astimezone(zone).replace(tzinfo=None)
    try:
        if match.group("mon"):
            month = _MONTHS.get(match.group("mon").lower())
            if month is None:
                return None
            local = datetime(local_now.year, month, int(match.group("day")), hour, minute)
            if local < local_now - timedelta(days=1):
                local = local.replace(year=local.year + 1)
        else:
            local = local_now.replace(hour=hour, minute=minute, second=0, microsecond=0)
            if local < local_now - timedelta(minutes=1):
                local += timedelta(days=1)
    except ValueError:
        return None
    # Reject nonexistent or ambiguous wall times rather than guessing.
    early, late = local.replace(tzinfo=zone, fold=0), local.replace(tzinfo=zone, fold=1)
    if early.utcoffset() != late.utcoffset():
        return None
    if early.astimezone(timezone.utc).astimezone(zone).replace(tzinfo=None) != local:
        return None
    if now - timedelta(minutes=1) <= early <= now + timedelta(minutes=duration + 1):
        return early
    return None
