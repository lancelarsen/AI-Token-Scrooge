"""Secret storage through the operating system's credential store.

macOS uses the login Keychain and Linux uses the Secret Service (GNOME Keyring, KWallet).
There is deliberately no plaintext fallback: if no secure backend is available, GitHub
connections fail with an explanation instead of silently writing tokens to disk.
"""
from __future__ import annotations

import fcntl
import os
import threading
from contextlib import contextmanager
from pathlib import Path
from typing import Iterator, Optional, Protocol

from ..core.model import SourceHealth
from .cli import GUID, ensure_private_dir
from .errors import ProviderAccessError

SERVICE = "AI Token Scrooge"
MAX_SECRET = 65536


class SecretStore(Protocol):
    def save(self, credential_id: str, value: str) -> None: ...
    def read(self, credential_id: str) -> Optional[str]: ...
    def remove(self, credential_id: str) -> None: ...
    def operation(self, credential_id: str): ...


def _check_id(credential_id: str) -> str:
    if not GUID.match(credential_id or ""):
        raise ValueError("Invalid credential identity.")
    return credential_id


class _Gates:
    def __init__(self):
        self._lock = threading.Lock()
        self._gates: dict[str, threading.RLock] = {}

    def get(self, key: str) -> threading.RLock:
        with self._lock:
            return self._gates.setdefault(key, threading.RLock())


class KeyringSecretStore:
    """Per-credential operations are leased in-process and across processes (flock), so renewal
    and removal cannot interleave between two running copies of Scrooge."""

    def __init__(self, security_dir: Path, namespace: str):
        self._locks = ensure_private_dir(Path(security_dir) / "Locks")
        self._namespace = namespace
        self._gates = _Gates()

    def _keyring(self):
        try:
            import keyring
            from keyring.backends import fail
        except ImportError as error:  # pragma: no cover - dependency is declared
            raise ProviderAccessError("Install the Python 'keyring' package to store GitHub sign-ins securely.",
                                      SourceHealth.Unsupported) from error
        backend = keyring.get_keyring()
        name = type(backend).__module__ + "." + type(backend).__name__
        if isinstance(backend, fail.Keyring) or "null" in name.lower() or "plaintext" in name.lower():
            raise ProviderAccessError("No secure credential store is available. Unlock or install your system keychain "
                                      "(Keychain on macOS, GNOME Keyring or KWallet on Linux) and try again.",
                                      SourceHealth.Unsupported)
        return keyring

    def _user(self, credential_id: str) -> str:
        return f"{self._namespace}:{_check_id(credential_id)}"

    @contextmanager
    def operation(self, credential_id: str) -> Iterator[None]:
        gate = self._gates.get(_check_id(credential_id))
        with gate:
            path = self._locks / f"{credential_id}.lock"
            fd = os.open(path, os.O_RDWR | os.O_CREAT | getattr(os, "O_NOFOLLOW", 0), 0o600)
            try:
                fcntl.flock(fd, fcntl.LOCK_EX)
                yield
            finally:
                fcntl.flock(fd, fcntl.LOCK_UN)
                os.close(fd)

    def save(self, credential_id: str, value: str) -> None:
        if len(value.encode()) > MAX_SECRET:
            raise ValueError("Credential exceeds the storage limit.")
        try:
            self._keyring().set_password(SERVICE, self._user(credential_id), value)
        except ProviderAccessError:
            raise
        except Exception as error:  # keyring backends raise varied errors
            raise ProviderAccessError("Your system keychain could not save the sign-in. Unlock it and try again.",
                                      SourceHealth.Stale) from error

    def read(self, credential_id: str) -> Optional[str]:
        try:
            value = self._keyring().get_password(SERVICE, self._user(credential_id))
        except ProviderAccessError:
            raise
        except Exception as error:
            raise ProviderAccessError("Your system keychain could not read the saved sign-in. Unlock it, then refresh.",
                                      SourceHealth.AuthenticationRequired) from error
        if value is not None and len(value.encode()) > MAX_SECRET:
            raise ProviderAccessError("The saved credential is invalid. Reconnect this account.", SourceHealth.AuthenticationRequired)
        return value

    def remove(self, credential_id: str) -> None:
        keyring = self._keyring()
        try:
            keyring.delete_password(SERVICE, self._user(credential_id))
        except keyring.errors.PasswordDeleteError:
            pass


class MemorySecretStore:
    """For tests and demo mode only."""

    def __init__(self):
        self.values: dict[str, str] = {}
        self._gates = _Gates()

    @contextmanager
    def operation(self, credential_id: str):
        with self._gates.get(credential_id):
            yield

    def save(self, credential_id: str, value: str) -> None:
        self.values[_check_id(credential_id)] = value

    def read(self, credential_id: str) -> Optional[str]:
        return self.values.get(_check_id(credential_id))

    def remove(self, credential_id: str) -> None:
        self.values.pop(credential_id, None)
