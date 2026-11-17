"""OpenAI Codex through `codex app-server` stdio (port of CodexConnection + ProviderMapping.Codex).

Only an explicit allowlist of account/usage methods can be called: no thread, inference, or
credit-redemption method is reachable from the monitor.
"""
from __future__ import annotations

import json
import os
import subprocess
import threading
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Optional

from ..core.model import (AllowanceProvenance, ProviderAccount, QuotaSnapshot, QuotaWindow, ResetCredit, ResetReserve,
                          SourceHealth, UsageUnit)
from .cli import locate, sanitized_environment, validate_executable
from .errors import CancelToken, Cancelled, ProviderAccessError, UserFacingError

ALLOWED = {"initialize", "account/read", "account/rateLimits/read", "account/login/start", "account/login/cancel", "account/logout"}
LINE_LIMIT = 2_000_000
REQUEST_TIMEOUT = 40.0


def find_codex() -> str:
    found = locate("codex", "SCROOGE_CODEX")
    if not found:
        raise UserFacingError("Install the Codex CLI (npm install -g @openai/codex, or Homebrew) to connect an OpenAI account.")
    return found


class _Pending:
    def __init__(self):
        self.event = threading.Event()
        self.result: Any = None
        self.error: Optional[BaseException] = None


class CodexConnection:
    """Owns one isolated Codex process."""

    def __init__(self, executable: str, profile: Path):
        executable = validate_executable(executable)
        profile = Path(profile).resolve()
        env = sanitized_environment({"CODEX_HOME": str(profile)})
        args = [executable, "app-server", "--stdio", "-c", 'cli_auth_credentials_store="keyring"', "-c", "mcp_servers={}",
                "-c", "analytics.enabled=false"]
        self._process = subprocess.Popen(args, cwd=str(profile), env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                         stderr=subprocess.DEVNULL, start_new_session=True)
        self._lock = threading.Lock()
        self._write_lock = threading.Lock()
        self._pending: dict[int, _Pending] = {}
        self._next = 0
        self._closed = threading.Event()
        self.on_notification: Optional[Callable[[dict], None]] = None
        self._reader = threading.Thread(target=self._read_loop, name="codex-reader", daemon=True)
        self._reader.start()

    @classmethod
    def start(cls, executable: str, profile: Path, token: CancelToken) -> "CodexConnection":
        connection = cls(executable, profile)
        try:
            connection.call("initialize", {"clientInfo": {"name": "ai_token_scrooge", "title": "AI Token Scrooge", "version": "0.1.0"},
                                           "capabilities": {"experimentalApi": True}}, token)
            connection._write({"method": "initialized"})
            return connection
        except BaseException:
            connection.close()
            raise

    def _write(self, message: dict) -> None:
        data = (json.dumps(message, separators=(",", ":")) + "\n").encode()
        with self._write_lock:
            if self._process.stdin is None or self._closed.is_set():
                raise ProviderAccessError("Codex connection ended. Reconnect to continue monitoring.", SourceHealth.Offline)
            try:
                self._process.stdin.write(data)
                self._process.stdin.flush()
            except OSError as error:
                raise ProviderAccessError("Codex connection ended. Reconnect to continue monitoring.", SourceHealth.Offline) from error

    def call(self, method: str, params: Any = None, token: Optional[CancelToken] = None) -> Any:
        if method not in ALLOWED:
            raise PermissionError("This method is not allowed by the usage monitor.")
        deadline = (token or CancelToken()).child(REQUEST_TIMEOUT)
        with self._lock:
            self._next += 1
            request_id = self._next
            pending = self._pending[request_id] = _Pending()
        try:
            self._write({"id": request_id, "method": method, "params": params})
            while not pending.event.wait(0.1):
                if self._closed.is_set():
                    raise ProviderAccessError("Codex connection ended. Reconnect to continue monitoring.", SourceHealth.Offline)
                if deadline.cancelled:
                    raise Cancelled("Codex did not answer in time.")
            if pending.error:
                raise pending.error
            return pending.result
        finally:
            with self._lock:
                self._pending.pop(request_id, None)

    def _read_loop(self) -> None:
        stream = self._process.stdout
        try:
            while stream is not None:
                line = stream.readline(LINE_LIMIT + 1)
                if not line:
                    break
                if len(line) > LINE_LIMIT:
                    break
                try:
                    root = json.loads(line)
                except json.JSONDecodeError:
                    break
                if not isinstance(root, dict):
                    break
                rid = root.get("id")
                with self._lock:
                    pending = self._pending.get(rid) if isinstance(rid, int) else None
                if pending is not None:
                    if "error" in root:
                        pending.error = ProviderAccessError("Codex could not complete this monitoring request. Check sign-in and retry.",
                                                            SourceHealth.AuthenticationRequired)
                    else:
                        pending.result = root.get("result")
                    pending.event.set()
                elif "method" in root and self.on_notification:
                    try:
                        self.on_notification(root)
                    except Exception:  # noqa: BLE001
                        pass
        finally:
            self._closed.set()
            with self._lock:
                for pending in self._pending.values():
                    pending.error = pending.error or ProviderAccessError(
                        "Codex connection ended. Reconnect to continue monitoring.", SourceHealth.Offline)
                    pending.event.set()

    def close(self) -> None:
        self._closed.set()
        try:
            if self._process.stdin:
                self._process.stdin.close()
        except OSError:
            pass
        if self._process.poll() is None:
            try:
                os.killpg(self._process.pid, 15)
            except (ProcessLookupError, PermissionError):
                pass
            try:
                self._process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                try:
                    os.killpg(self._process.pid, 9)
                except (ProcessLookupError, PermissionError):
                    pass
        self._reader.join(timeout=5)

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()


# ------------------------------------------------------------------ mapping

def text(obj: Any, key: str) -> Optional[str]:
    value = obj.get(key) if isinstance(obj, dict) else None
    return value if isinstance(value, str) else None


def number(obj: Any, key: str) -> Optional[float]:
    value = obj.get(key) if isinstance(obj, dict) else None
    return float(value) if isinstance(value, (int, float)) and not isinstance(value, bool) else None


def integer(obj: Any, key: str) -> Optional[int]:
    value = number(obj, key)
    return int(value) if value is not None and 0 <= value <= 2**31 - 1 and value == int(value) else None


def timestamp(obj: Any, key: str) -> Optional[datetime]:
    seconds = number(obj, key)
    if seconds is None or seconds != int(seconds) or not -62135596800 <= seconds <= 253402300799:
        return None
    try:
        return datetime.fromtimestamp(int(seconds), tz=timezone.utc)
    except (OverflowError, OSError, ValueError):
        return None


def map_codex(account: ProviderAccount, response: Any, observed: datetime) -> QuotaSnapshot:
    if not isinstance(response, dict):
        raise UserFacingError("Codex returned an unreadable usage response.")
    scope = text(response, "accountId")
    if scope and account.Scope != "unresolved" and scope != account.Scope:
        raise UserFacingError("The provider returned a different workspace. Reconnect this account.")
    windows: list[QuotaWindow] = []

    def add_bucket(bucket_id: str, bucket: Any) -> None:
        if not isinstance(bucket, dict):
            return
        for key in ("primary", "secondary"):
            window = bucket.get(key)
            if not isinstance(window, dict):
                continue
            minutes = integer(window, "windowDurationMins") or None
            duration = {300: "5-hour", 10080: "Weekly"}.get(minutes) if minutes else None
            duration = duration or (f"{minutes} min" if minutes else key)
            name = text(bucket, "limitName") or ("Codex" if bucket_id == "codex" else bucket_id)
            windows.append(QuotaWindow(f"{bucket_id}/{key}", f"{name} · {duration}", UsageUnit.Percent, number(window, "usedPercent"),
                                       100, timestamp(window, "resetsAt"), minutes, AllowanceProvenance.Provider))

    by_id = response.get("rateLimitsByLimitId")
    if isinstance(by_id, dict) and by_id:
        for bucket_id, bucket in by_id.items():
            add_bucket(bucket_id, bucket)
    elif isinstance(response.get("rateLimits"), dict):
        legacy = response["rateLimits"]
        add_bucket(text(legacy, "limitId") or "codex", legacy)

    reserve = None
    credits = response.get("rateLimitResetCredits")
    if isinstance(credits, dict):
        details = None
        if isinstance(credits.get("credits"), list):
            seen: dict[str, ResetCredit] = {}
            for c in credits["credits"]:
                cid = text(c, "id")
                if cid is None:
                    raise UserFacingError("Reset credit identity missing.")
                seen.setdefault(cid, ResetCredit(cid, text(c, "status") or "unknown", timestamp(c, "expiresAt"), text(c, "title")))
            details = tuple(seen.values())
        reserve = ResetReserve(integer(credits, "availableCount"), details)

    message = "Workspace identity unavailable from provider" if scope is None else None
    allowed = response.get("ordinaryUsageAllowed")
    usage_allowed = allowed if isinstance(allowed, bool) else None
    if allowed is False:
        message = "Provider reports included usage is paused"
    has_reserve = reserve is not None and (reserve.AvailableCount is not None or bool(reserve.Credits))
    if not windows and has_reserve:
        message = "Codex reported reset credits; quota windows are unavailable."
    health = SourceHealth.Unsupported if not windows and not has_reserve else SourceHealth.Healthy
    return QuotaSnapshot(account.Id, account.Subject, scope or account.Scope, observed, tuple(windows), reserve, health,
                         message, UsageAllowed=usage_allowed)


def valid_auth_url(url: Optional[str]) -> bool:
    from urllib.parse import urlparse
    if not url:
        return False
    parsed = urlparse(url)
    host = (parsed.hostname or "").lower()
    return parsed.scheme == "https" and (host == "auth.openai.com" or host.endswith(".openai.com"))
