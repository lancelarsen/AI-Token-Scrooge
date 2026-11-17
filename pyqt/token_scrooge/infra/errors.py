"""Shared provider errors and cooperative cancellation."""
from __future__ import annotations

import threading
import time
from datetime import datetime
from typing import Callable, Optional

from ..core.model import SourceHealth


class ProviderAccessError(Exception):
    def __init__(self, message: str, health: SourceHealth, retry_at: Optional[datetime] = None):
        super().__init__(message)
        self.health = health
        self.retry_at = retry_at


class Cancelled(Exception):
    pass


class CancelToken:
    """Event + optional deadline. Child tokens observe their parent's cancellation."""

    def __init__(self, timeout: Optional[float] = None, parent: Optional["CancelToken"] = None):
        self._event = threading.Event()
        self._deadline = time.monotonic() + timeout if timeout is not None else None
        self._parent = parent
        self._callbacks: list[Callable[[], None]] = []
        self._lock = threading.Lock()

    def child(self, timeout: Optional[float] = None) -> "CancelToken":
        return CancelToken(timeout, self)

    def cancel(self) -> None:
        with self._lock:
            if self._event.is_set():
                return
            self._event.set()
            callbacks, self._callbacks = self._callbacks, []
        for cb in callbacks:
            try:
                cb()
            except Exception:  # noqa: BLE001 - cleanup callbacks must not mask cancellation
                pass

    def on_cancel(self, callback: Callable[[], None]) -> None:
        with self._lock:
            if not self._event.is_set():
                self._callbacks.append(callback)
                return
        callback()

    @property
    def cancelled(self) -> bool:
        if self._event.is_set():
            return True
        if self._deadline is not None and time.monotonic() >= self._deadline:
            self.cancel()
            return True
        if self._parent is not None and self._parent.cancelled:
            self.cancel()
            return True
        return False

    def remaining(self) -> Optional[float]:
        values = []
        if self._deadline is not None:
            values.append(self._deadline - time.monotonic())
        if self._parent is not None and (r := self._parent.remaining()) is not None:
            values.append(r)
        return max(0.0, min(values)) if values else None

    def check(self) -> None:
        if self.cancelled:
            raise Cancelled("The operation timed out or was canceled.")

    def wait(self, seconds: float) -> None:
        """Sleep up to `seconds`, raising promptly if canceled."""
        end = time.monotonic() + seconds
        while True:
            self.check()
            left = end - time.monotonic()
            if left <= 0:
                return
            self._event.wait(min(left, 0.25))


NEVER = CancelToken()


def safe_message(error: BaseException) -> str:
    """User-facing text that never echoes raw provider output or secrets."""
    if isinstance(error, (ProviderAccessError, UserFacingError)):
        return str(error)
    if isinstance(error, Cancelled):
        return "The connection timed out or was canceled. Try again."
    return "Could not complete the connection. Check your connection and provider sign-in, then retry."


class UserFacingError(Exception):
    """Validation or setup failure whose message is safe to show as-is."""
