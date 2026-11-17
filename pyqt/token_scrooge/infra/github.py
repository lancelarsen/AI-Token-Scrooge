"""GitHub Copilot: device-flow sign-in, billing usage, credential renewal and mapping.

Port of GitHubDeviceFlow, GitHubUsageClient, GitHubCredentialManager and ProviderMapping.GitHub*.
Requests never follow redirects; credentials only travel in form bodies or the Authorization header.
"""
from __future__ import annotations

import email.utils
import json
import re
import threading
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from typing import Any, Callable, Optional

from ..core import serialization as ser
from ..core.model import (AllowanceProvenance, ProviderAccount, QuotaSnapshot, QuotaWindow, SourceHealth, UsageUnit,
                          fmt_count, replace)
from .errors import CancelToken, ProviderAccessError, UserFacingError

DEFAULT_CLIENT_ID = "Iv23lizZtIgGxr7FByW4"  # Public GitHub App client ID (not a secret).
USER_AGENT = "AIResetRadar/0.1"
API_VERSION = "2026-03-10"


def valid_client_id(value: Optional[str]) -> bool:
    return bool(value) and len(value) <= 128 and re.fullmatch(r"[A-Za-z0-9_.-]+", value) is not None


def valid_secret(value: Optional[str]) -> bool:
    return isinstance(value, str) and 0 < len(value) <= 4096 and all("!" <= c <= "~" for c in value)


def reconnect_error() -> ProviderAccessError:
    return ProviderAccessError("Reconnect GitHub to restore monitoring.", SourceHealth.AuthenticationRequired)


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):  # noqa: D401
        return None


_OPENER = urllib.request.build_opener(_NoRedirect)


@dataclass
class HttpResponse:
    status: int
    headers: dict[str, str]
    body: bytes


def http_request(url: str, *, method: str = "GET", headers: Optional[dict] = None, data: Optional[bytes] = None,
                 timeout: float = 35, limit: int = 5_000_000) -> HttpResponse:
    request = urllib.request.Request(url, data=data, method=method, headers=headers or {})
    try:
        response = _OPENER.open(request, timeout=timeout)
    except urllib.error.HTTPError as error:
        response = error
    except (urllib.error.URLError, TimeoutError, OSError) as error:
        raise ProviderAccessError("GitHub is unreachable. Last known values are retained.", SourceHealth.Offline) from error
    with response:
        length = response.headers.get("Content-Length")
        if length and length.isdigit() and int(length) > limit:
            raise UserFacingError("GitHub response exceeded the monitor's size limit.")
        body = response.read(limit + 1)
        if len(body) > limit:
            raise UserFacingError("GitHub response exceeded the monitor's size limit.")
        return HttpResponse(response.status if hasattr(response, "status") else response.code,
                            {k.lower(): v for k, v in response.headers.items()}, body)


Transport = Callable[..., HttpResponse]


# ------------------------------------------------------------------ device flow

@dataclass(frozen=True)
class GitHubAppTokens:
    ClientId: str
    AccessToken: str
    RefreshToken: str
    ExpiresAt: datetime
    RefreshExpiresAt: datetime

    def __repr__(self) -> str:  # Never print secrets.
        return "GitHubAppTokens(<redacted>)"


@dataclass(frozen=True)
class DeviceChallenge:
    client_id: str
    device_code: str
    user_code: str
    expires_at: datetime
    interval: float
    verification_uri: str = "https://github.com/login/device"

    def __repr__(self) -> str:
        return "DeviceChallenge(<redacted>)"


class GitHubDeviceFlow:
    def __init__(self, transport: Transport = http_request, clock=None):
        self.transport = transport
        self.clock = clock or (lambda: datetime.now(timezone.utc))

    def _post(self, path: str, fields: dict[str, str]) -> dict:
        response = self.transport("https://github.com" + path, method="POST", limit=65536,
                                  headers={"Accept": "application/json", "User-Agent": USER_AGENT,
                                           "Content-Type": "application/x-www-form-urlencoded"},
                                  data=urllib.parse.urlencode(fields).encode())
        if not 200 <= response.status < 300:
            raise ProviderAccessError("GitHub sign-in is temporarily unavailable. Try again.", SourceHealth.Offline)
        try:
            data = json.loads(response.body)
        except (json.JSONDecodeError, UnicodeDecodeError):
            raise UserFacingError("GitHub returned an invalid sign-in response. Try again.") from None
        if not isinstance(data, dict):
            raise UserFacingError("GitHub returned an invalid sign-in response. Try again.")
        return data

    @staticmethod
    def _number(data: dict, name: str, low: int, high: int) -> int:
        value = data.get(name)
        if isinstance(value, int) and not isinstance(value, bool) and low <= value <= high:
            return value
        raise UserFacingError("GitHub returned invalid sign-in timing. Try again.")

    @staticmethod
    def _raise_error(data: dict) -> None:
        error = data.get("error")
        if error is None:
            return
        raise ProviderAccessError({
            "access_denied": "GitHub sign-in was declined. You can try again.",
            "expired_token": "The GitHub sign-in code expired. Start sign-in again.",
            "token_expired": "The GitHub sign-in code expired. Start sign-in again.",
            "device_flow_disabled": "GitHub browser sign-in needs the app registration configured by Scrooge's developer.",
            "incorrect_client_credentials": "GitHub browser sign-in needs the app registration configured by Scrooge's developer.",
            "bad_refresh_token": "GitHub sign-in expired. Reconnect this account.",
        }.get(error, "GitHub could not complete sign-in. Try again."), SourceHealth.AuthenticationRequired)

    def begin(self, client_id: str) -> DeviceChallenge:
        if not valid_client_id(client_id):
            raise UserFacingError("GitHub browser sign-in is not configured in this build.")
        now = self.clock()
        data = self._post("/login/device/code", {"client_id": client_id, "scope": "offline_access"})
        self._raise_error(data)
        uri, code, device = data.get("verification_uri"), data.get("user_code"), data.get("device_code")
        if (uri != "https://github.com/login/device" or not isinstance(code, str)
                or not re.fullmatch(r"[A-Z0-9]{4}-[A-Z0-9]{4}", code) or not valid_secret(device)):
            raise UserFacingError("GitHub returned an invalid sign-in request. Try again.")
        lifetime = self._number(data, "expires_in", 1, 1800)
        interval = self._number(data, "interval", 1, 300) if "interval" in data else 5
        return DeviceChallenge(client_id, device, code, now + timedelta(seconds=lifetime), interval)

    def wait(self, challenge: DeviceChallenge, token: CancelToken) -> GitHubAppTokens:
        interval = challenge.interval
        while True:
            if challenge.expires_at - self.clock() <= timedelta(seconds=interval):
                raise ProviderAccessError("The GitHub sign-in code expired. Start sign-in again.", SourceHealth.AuthenticationRequired)
            token.wait(interval)
            data = self._post("/login/oauth/access_token", {"client_id": challenge.client_id, "device_code": challenge.device_code,
                                                             "grant_type": "urn:ietf:params:oauth:grant-type:device_code"})
            error = data.get("error")
            if error == "authorization_pending":
                continue
            if error == "slow_down":
                nxt = data.get("interval")
                interval = max(interval + 5, nxt if isinstance(nxt, int) and 0 < nxt <= 1800 else 0)
                continue
            self._raise_error(data)
            return self._tokens(data, challenge.client_id)

    def refresh(self, previous: GitHubAppTokens) -> GitHubAppTokens:
        if not valid_client_id(previous.ClientId) or not valid_secret(previous.RefreshToken):
            raise reconnect_error()
        data = self._post("/login/oauth/access_token", {"client_id": previous.ClientId, "grant_type": "refresh_token",
                                                         "refresh_token": previous.RefreshToken})
        self._raise_error(data)
        return self._tokens(data, previous.ClientId)

    def _tokens(self, data: dict, client_id: str) -> GitHubAppTokens:
        access, refresh = data.get("access_token"), data.get("refresh_token")
        if not valid_secret(access) or not valid_secret(refresh) or str(data.get("token_type", "")).lower() != "bearer":
            raise UserFacingError("GitHub returned incomplete sign-in credentials. Sign in again.")
        now = self.clock()
        return GitHubAppTokens(client_id, access, refresh, now + timedelta(seconds=self._number(data, "expires_in", 1, 86400)),
                               now + timedelta(seconds=self._number(data, "refresh_token_expires_in", 1, 366 * 86400)))


# ------------------------------------------------------------------ usage client

class GitHubUsageClient:
    def __init__(self, transport: Transport = http_request, clock=None):
        self.transport = transport
        self.clock = clock or (lambda: datetime.now(timezone.utc))

    def _get(self, path: str, token: str) -> Any:
        response = self.transport("https://api.github.com/" + path, headers={
            "Authorization": f"Bearer {token}", "User-Agent": USER_AGENT, "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": API_VERSION})
        now = self.clock()
        retry = None
        after = response.headers.get("retry-after")
        if after:
            if after.strip().isdigit():
                retry = now + timedelta(seconds=int(after))
            else:
                try:
                    retry = email.utils.parsedate_to_datetime(after)
                except (TypeError, ValueError):
                    retry = None
        limited = response.status == 429 or (response.status == 403 and (
            retry is not None or response.headers.get("x-ratelimit-remaining") == "0"))
        if limited:
            reset = response.headers.get("x-ratelimit-reset", "")
            if retry is None and reset.isdigit() and 0 < int(reset) <= 253402300799:
                retry = datetime.fromtimestamp(int(reset), timezone.utc)
            raise ProviderAccessError("GitHub asked Scrooge to wait before refreshing.", SourceHealth.Stale,
                                      retry if retry and retry > now else now + timedelta(minutes=5))
        if response.status == 401:
            raise ProviderAccessError("GitHub sign-in expired. Reconnect this account.", SourceHealth.AuthenticationRequired)
        if response.status in (403, 404):
            raise ProviderAccessError("GitHub billing access is unavailable. Personal tokens need Plan read permission; "
                                      "organization billing needs authorized administration access.", SourceHealth.PermissionRequired, retry)
        if not 200 <= response.status < 300:
            raise ProviderAccessError("GitHub usage is temporarily unavailable. Last known values are retained.", SourceHealth.Offline)
        try:
            return json.loads(response.body)
        except (json.JSONDecodeError, UnicodeDecodeError):
            raise UserFacingError("GitHub returned an unreadable report.") from None

    def identify(self, token: str) -> tuple[str, str]:
        user = self._get("user", token)
        uid = user.get("id") if isinstance(user, dict) else None
        login = user.get("login") if isinstance(user, dict) else None
        if not isinstance(uid, int) or isinstance(uid, bool) or not isinstance(login, str):
            raise UserFacingError("GitHub did not return an account identity.")
        return str(uid), login

    def read(self, account: ProviderAccount, token: str) -> QuotaSnapshot:
        subject, login = self.identify(token)
        if subject != account.Subject:
            raise ProviderAccessError("This token belongs to a different GitHub account. Reconnect the account.",
                                      SourceHealth.AuthenticationRequired)
        now = self.clock()
        kind = "premium_request" if account.Unit == UsageUnit.Requests else "ai_credit"
        q = urllib.parse.quote
        if account.Scope.startswith("organization:"):
            path = (f"organizations/{q(account.Scope[13:], safe='')}/settings/billing/{kind}/usage"
                    f"?year={now.year}&month={now.month}&user={q(login, safe='')}")
        else:
            path = f"users/{q(login, safe='')}/settings/billing/{kind}/usage?year={now.year}&month={now.month}"
        allowance = plan = None
        if account.Scope == "personal":
            try:
                entitlement = self._get("copilot_internal/user", token)
                managed = map_managed_quota(account, entitlement, now)
                if managed is not None:
                    return managed
                allowance = github_allowance(entitlement, account.Unit)
                if isinstance(entitlement, dict) and isinstance(entitlement.get("copilot_plan"), str):
                    plan = entitlement["copilot_plan"]
            except ProviderAccessError as error:
                # The internal allowance lookup is optional; it must not block the official report.
                if error.retry_at is not None or error.health not in (SourceHealth.PermissionRequired, SourceHealth.Offline):
                    raise
            except UserFacingError:
                pass
        return replace(map_github(account, self._get(path, token), now, allowance), PlanType=plan)


def _next_month(observed: datetime) -> datetime:
    utc = observed.astimezone(timezone.utc)
    return datetime(utc.year + (utc.month == 12), utc.month % 12 + 1, 1, tzinfo=timezone.utc)


def _nonnegative(obj: dict, key: str) -> Optional[float]:
    value = obj.get(key)
    try:
        number = float(value) if isinstance(value, (int, float, str)) and not isinstance(value, bool) else None
    except ValueError:
        return None
    return number if number is not None and number >= 0 else None


def map_managed_quota(account: ProviderAccount, response: Any, observed: datetime) -> Optional[QuotaSnapshot]:
    """Business/Enterprise usage and its personal budget come from the same user quota."""
    if (account.Scope != "personal" or not isinstance(response, dict) or response.get("copilot_plan") not in ("business", "enterprise")
            or response.get("token_based_billing") is not True or account.Unit != UsageUnit.Credits):
        return None
    quota = (response.get("quota_snapshots") or {}).get("premium_interactions") if isinstance(response.get("quota_snapshots"), dict) else None
    if not isinstance(quota, dict) or not isinstance(quota.get("unlimited"), bool):
        return None
    pooled = quota["unlimited"]
    limit = None if pooled else _nonnegative(quota, "entitlement")
    used = _nonnegative(quota, "credits_used")
    if used is None and not pooled and limit is not None:
        remaining = _nonnegative(quota, "quota_remaining")
        if remaining is not None and remaining <= limit:
            used = limit - remaining
    blocked = pooled and quota.get("has_quota") is False
    message = ("Your organization has blocked Copilot usage. Contact your administrator." if blocked else
               "No personal limit is set. Usage draws from your organization's shared budget." if pooled else
               f"Personal budget: {fmt_count(limit)} AI credits. Usage reported by GitHub." if limit is not None else
               "GitHub did not report a personal limit.")
    return QuotaSnapshot(account.Id, account.Subject, account.Scope, observed, (
        QuotaWindow("copilot/monthly", "AI credits · Monthly", UsageUnit.Credits, used, limit, _next_month(observed), None,
                    AllowanceProvenance.Provider if limit is not None else AllowanceProvenance.Unknown),), None,
        Message=message, UsageAllowed=False if blocked else None, PlanType=response.get("copilot_plan"))


def github_allowance(response: Any, unit: UsageUnit) -> Optional[float]:
    if not isinstance(response, dict) or response.get("copilot_plan") not in ("individual", "individual_pro", "individual_max", "individual_edu"):
        return None
    billing = response.get("token_based_billing")
    if not isinstance(billing, bool) or (UsageUnit.Credits if billing else UsageUnit.Requests) != unit:
        return None
    snapshots = response.get("quota_snapshots")
    quota = snapshots.get("premium_interactions") if isinstance(snapshots, dict) else None
    if not isinstance(quota, dict) or quota.get("unlimited") is not False or "entitlement" not in quota:
        return None
    return _nonnegative(quota, "entitlement")


def map_github(account: ProviderAccount, response: Any, observed: datetime, provider_allowance: Optional[float] = None) -> QuotaSnapshot:
    items = response.get("usageItems") if isinstance(response, dict) else None
    if not isinstance(items, list):
        raise UserFacingError("Billing response did not include usage data.")
    used: Optional[float] = 0.0
    matched = unknown_units = False
    for item in items:
        unit = item.get("unitType") if isinstance(item, dict) else None
        selected = unit == "requests" if account.Unit == UsageUnit.Requests else unit in ("credits", "ai-credits")
        if unit not in ("requests", "credits", "ai-credits"):
            unknown_units = True
        if selected:
            quantity = item.get("grossQuantity")
            if not isinstance(quantity, (int, float)) or isinstance(quantity, bool) or quantity < 0:
                raise UserFacingError("Billing quantity is unavailable or invalid.")
            used += quantity
            matched = True
    if unknown_units or (items and not matched):
        used = None
    label = "Premium requests · Monthly" if account.Unit == UsageUnit.Requests else "AI credits · Monthly"
    limit = provider_allowance if provider_allowance is not None else account.Allowance
    source = (AllowanceProvenance.Provider if provider_allowance is not None else
              AllowanceProvenance.Manual if account.Allowance is not None else AllowanceProvenance.Unknown)
    message = ("The report did not establish usage in the selected unit. Capacity is unavailable." if used is None else
               "Allowance reported by GitHub; billing usage may lag." if provider_allowance is not None else
               "Automatic allowance unavailable; a user-entered allowance is used when supplied. Billing reports may lag.")
    return QuotaSnapshot(account.Id, account.Subject, account.Scope, observed, (
        QuotaWindow("copilot/monthly", label, account.Unit, used, limit, _next_month(observed), None, source),), None, Message=message)


# ------------------------------------------------------------------ credential manager

PREFIX = "github-app:v1:"


@dataclass(frozen=True)
class _Envelope:
    Version: int
    Subject: str
    Tokens: GitHubAppTokens
    NeedsVerification: bool = False
    RenewalStarted: bool = False


def _encode(record: _Envelope) -> str:
    return PREFIX + ser.dumps(record)


def _validate(record: _Envelope) -> None:
    t = record.Tokens
    if (record.Version != 1 or not record.Subject or t is None or not valid_client_id(t.ClientId)
            or not valid_secret(t.AccessToken) or not valid_secret(t.RefreshToken)):
        raise reconnect_error()


class GitHubCredentialManager:
    """Owns read/renew/verify/save as one leased operation. Before remote rotation it persists a
    renewal-started marker; a crash after rotation requires reconnecting rather than retrying a
    possibly consumed refresh token."""

    def __init__(self, secrets, usage: GitHubUsageClient, flow: Optional[GitHubDeviceFlow], clock=None):
        self.secrets, self.usage, self.flow = secrets, usage, flow
        self.clock = clock or (lambda: datetime.now(timezone.utc))
        self._pending: dict[str, tuple[_Envelope, str]] = {}
        self._lock = threading.Lock()

    def save_app(self, credential_id: str, subject: str, tokens: GitHubAppTokens) -> None:
        record = _Envelope(1, subject, tokens)
        _validate(record)
        with self.secrets.operation(credential_id):
            self.secrets.save(credential_id, _encode(record))
            self._pending.pop(credential_id, None)

    def save_token(self, credential_id: str, token: str) -> None:
        with self.secrets.operation(credential_id):
            self.secrets.save(credential_id, token)

    def read_access_token(self, account: ProviderAccount, token: CancelToken) -> str:
        cid = account.credential
        with self.secrets.operation(cid):
            if cid in self._pending:
                self._persist_replacement(cid, *self._pending[cid])
            token.check()
            saved = self.secrets.read(cid)
            if saved is None or len(saved) > 32768:
                raise reconnect_error()
            if not saved.startswith(PREFIX):
                if saved.startswith("github-app:") or not valid_secret(saved) or saved.startswith("{"):
                    raise reconnect_error()
                return saved  # A protected personal access token, not an app credential.
            try:
                record = ser.loads(_Envelope, saved[len(PREFIX):])
            except (ValueError, KeyError, TypeError):
                raise reconnect_error() from None
            _validate(record)
            if record.Subject != account.Subject or record.RenewalStarted:
                raise reconnect_error()
            now = self.clock()
            if record.Tokens.ExpiresAt <= now + timedelta(minutes=2):
                if self.flow is None or record.Tokens.RefreshExpiresAt <= now:
                    raise reconnect_error()
                token.check()
                marker = _encode(replace(record, RenewalStarted=True))
                self.secrets.save(cid, marker)
                rotated = self.flow.refresh(record.Tokens)
                record = replace(record, Tokens=rotated, NeedsVerification=True, RenewalStarted=False)
                self._pending[cid] = (record, marker)
                self._persist_replacement(cid, record, marker)
            token.check()
            if record.NeedsVerification:
                subject, _ = self.usage.identify(record.Tokens.AccessToken)
                if subject != account.Subject:
                    raise reconnect_error()
                record = replace(record, NeedsVerification=False)
                self.secrets.save(cid, _encode(record))
            return record.Tokens.AccessToken

    def _persist_replacement(self, cid: str, replacement: _Envelope, expected: str) -> None:
        saved = self.secrets.read(cid)
        encoded = _encode(replacement)
        # Another process may have removed or replaced the credential; never restore it from memory.
        if saved != expected and saved != encoded:
            self._pending.pop(cid, None)
            raise reconnect_error()
        self.secrets.save(cid, encoded)
        self._pending.pop(cid, None)

    def remove(self, credential_id: str) -> None:
        with self.secrets.operation(credential_id):
            self.secrets.remove(credential_id)
            self._pending.pop(credential_id, None)
