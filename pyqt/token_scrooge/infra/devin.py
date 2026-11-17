"""Devin Enterprise My Analytics (port of DevinClient/DevinUsage). The browser that reads the page
is supplied by the app layer (QtWebEngine) with one persistent profile per connection."""
from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from typing import Optional, Protocol, Sequence
from urllib.parse import urlsplit
from zoneinfo import ZoneInfo, ZoneInfoNotFoundError

from ..core.model import (AllowanceProvenance, CredentialProfileLocation, ProviderAccount, QuotaSnapshot, QuotaWindow,
                          SourceHealth, UsageUnit)
from .cli import ProfilePaths
from .errors import CancelToken, ProviderAccessError, UserFacingError

LOCATION = CredentialProfileLocation.LocalSecurityDirectory


@dataclass(frozen=True)
class DevinReading:
    Subject: str
    Used: Optional[float]
    Limit: Optional[float]
    Reset: Optional[datetime]


class DevinBrowser(Protocol):
    def read(self, origin: str, profile: str, interactive: bool, token: CancelToken) -> DevinReading: ...


def workspace(value: str) -> str:
    """Keep only an HTTPS tenant origin on devinenterprise.com; discard path, query and hints."""
    text = (value or "").strip()
    if "://" not in text:
        text = "https://" + text
    try:
        parts = urlsplit(text)
        port = parts.port
    except ValueError:
        raise UserFacingError("Enter your company's Devin Enterprise workspace URL.") from None
    host = (parts.hostname or "").lower()
    if (parts.scheme != "https" or port not in (None, 443) or "@" in parts.netloc
            or not re.fullmatch(r"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.devinenterprise\.com", host)):
        raise UserFacingError("Enter your company's Devin Enterprise workspace URL.")
    return f"https://{host}"


def _number(text: Optional[str]) -> Optional[float]:
    if not text or len(text) > 40 or not re.fullmatch(r"(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?", text):
        return None
    return float(text.replace(",", ""))


def _reset(display: Optional[str], zone_id: Optional[str], observed: datetime) -> Optional[datetime]:
    if not display or not display.strip() or not zone_id:
        return None
    try:
        zone = ZoneInfo(zone_id)
    except (ZoneInfoNotFoundError, ValueError):
        return None
    match = re.fullmatch(r"\s*(\d{1,2})/(\d{1,2})\s+(\d{1,2}):(\d{2})\s*([AP]M)\s*", display, re.IGNORECASE)
    if not match:
        return None
    month, day, hour, minute, ap = int(match[1]), int(match[2]), int(match[3]), int(match[4]), match[5].upper()
    if not 1 <= hour <= 12:
        return None
    hour = hour % 12 + (12 if ap == "PM" else 0)
    local_year = observed.astimezone(zone).year
    for year in (local_year, local_year + 1):
        try:
            local = datetime(year, month, day, hour, minute)
        except ValueError:
            continue
        early, late = local.replace(tzinfo=zone, fold=0), local.replace(tzinfo=zone, fold=1)
        if early.utcoffset() != late.utcoffset() or early.astimezone(timezone.utc).astimezone(zone).replace(tzinfo=None) != local:
            continue
        reset = early.astimezone(timezone.utc)
        if observed < reset <= observed + timedelta(days=32):
            return reset
    return None


def parse_usage(subject: str, fields: Sequence[str], observed: datetime) -> DevinReading:
    if len(fields) != 4 or _number(fields[0]) is None:
        raise ProviderAccessError("Devin returned an unreadable ACU total.", SourceHealth.Unsupported)
    return DevinReading(subject, _number(fields[0]), _number(fields[1]), _reset(fields[2], fields[3], observed))


class DevinClient:
    def __init__(self, profiles: ProfilePaths, secrets, browser: Optional[DevinBrowser], journal=None, clock=None):
        self.profiles, self.secrets, self.browser, self.journal = profiles, secrets, browser, journal
        self.clock = clock or (lambda: datetime.now(timezone.utc))

    def _read(self, origin: str, profile, interactive: bool, token: CancelToken) -> DevinReading:
        if self.browser is None:
            raise ProviderAccessError("Devin needs the PyQt6-WebEngine package. Install it, then restart Scrooge.",
                                      SourceHealth.Unsupported)
        return self.browser.read(origin, str(profile), interactive, token.child(600 if interactive else 45))

    def connect(self, account_id: str, credential_id: str, nickname: str, workspace_url: str,
                token: CancelToken) -> tuple[ProviderAccount, QuotaSnapshot]:
        origin = workspace(workspace_url)
        if self.journal is not None:
            self.journal.stage_credential("Devin", credential_id, LOCATION)
        profile = self.profiles.ensure("Devin", credential_id, LOCATION)
        with self.secrets.operation(credential_id):
            reading = self._read(origin, profile, True, token)
        account = ProviderAccount(account_id, "Devin", nickname, reading.Subject, origin[8:], Unit=UsageUnit.Acus,
                                  CredentialId=credential_id, PollingMinutes=10, ProfileLocation=LOCATION)
        return account, self._snapshot(account, reading)

    def refresh(self, account: ProviderAccount, token: CancelToken) -> QuotaSnapshot:
        if account.ProfileLocation != LOCATION or account.CredentialId is None:
            raise UserFacingError("Reconnect this Devin account.")
        with self.secrets.operation(account.CredentialId):
            profile = self.profiles.path_for("Devin", account.CredentialId, LOCATION)
            if not profile.is_dir():
                raise ProviderAccessError("Sign in to Devin again.", SourceHealth.AuthenticationRequired)
            reading = self._read(workspace(account.Scope), profile, False, token)
        if reading.Subject != account.Subject:
            raise ProviderAccessError("The Devin user changed. Sign in again with the original account.", SourceHealth.AuthenticationRequired)
        return self._snapshot(account, reading)

    def disconnect(self, account: ProviderAccount, token: CancelToken) -> None:
        if account.ProfileLocation != LOCATION or account.CredentialId is None:
            raise UserFacingError("Invalid Devin profile location.")
        with self.secrets.operation(account.CredentialId):
            self.profiles.delete("Devin", account.CredentialId, LOCATION)
        if self.journal is not None:
            self.journal.complete_credential_cleanup(account.CredentialId)

    def _snapshot(self, account: ProviderAccount, reading: DevinReading) -> QuotaSnapshot:
        return QuotaSnapshot(account.Id, account.Subject, account.Scope, self.clock(), (
            QuotaWindow("devin/monthly", "Monthly ACUs", UsageUnit.Acus, reading.Used, reading.Limit, reading.Reset,
                        AllowanceSource=AllowanceProvenance.Provider if reading.Limit is not None else AllowanceProvenance.Unknown),),
            None, SourceHealth.Healthy, PlanType="enterprise")
