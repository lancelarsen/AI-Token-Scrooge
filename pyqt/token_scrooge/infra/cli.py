"""CLI process boundary: executable discovery, environment filtering, bounded execution,
and Scrooge-owned profile directories (port of BoundedCliProcess/CliEnvironment/CliProfilePaths)."""
from __future__ import annotations

import hashlib
import os
import re
import shutil
import signal
import stat
import subprocess
import sys
import threading
import time
from pathlib import Path
from typing import Iterable, Optional, Sequence

from ..core.model import CredentialProfileLocation
from .errors import CancelToken, Cancelled, UserFacingError

_PREFIXES = ("OPENAI_", "CODEX_", "ANTHROPIC_", "CLAUDE_", "GITHUB_", "GH_", "AZURE_OPENAI_", "GEMINI_")
_EXCLUDED = {"GOOGLE_API_KEY", "GOOGLE_APPLICATION_CREDENTIALS", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY",
             "AWS_SESSION_TOKEN", "AWS_PROFILE", "AWS_SHARED_CREDENTIALS_FILE", "AWS_CONFIG_FILE",
             "NODE_OPTIONS", "NODE_PATH", "BUN_OPTIONS"}
OUTPUT_LIMIT = 2_000_000
GUID = re.compile(r"\A[0-9a-f]{32}\Z")


def sanitized_environment(extra: Optional[dict[str, str]] = None) -> dict[str, str]:
    """An explicit filter of provider credential/config overrides, not a guarantee every secret is removed."""
    env = {k: v for k, v in os.environ.items()
           if k.upper() not in _EXCLUDED and not k.upper().startswith(_PREFIXES)}
    env.update(extra or {})
    return env


def _candidate_dirs() -> list[Path]:
    home = Path.home()
    dirs = [home / ".local/bin", home / ".claude/local", home / ".npm-global/bin", home / ".bun/bin",
            home / ".volta/bin", home / ".cargo/bin", Path("/opt/homebrew/bin"), Path("/usr/local/bin"), Path("/usr/bin")]
    if sys.platform == "darwin":
        dirs += [Path("/Applications/Codex.app/Contents/Resources"), home / "Applications/Codex.app/Contents/Resources"]
    nvm = home / ".nvm/versions/node"
    if nvm.is_dir():
        dirs += sorted((p / "bin" for p in nvm.iterdir()), reverse=True)
    return dirs


def validate_executable(path: str | os.PathLike) -> str:
    """Resolve to an absolute regular file the user (or root) owns and others cannot modify."""
    raw = Path(path)
    if not raw.is_absolute():
        raise UserFacingError("The installed provider CLI could not be located. Reinstall the official CLI and try again.")
    real = Path(os.path.realpath(raw))
    try:
        info = real.stat()
    except OSError:
        raise UserFacingError("The installed provider CLI could not be located. Reinstall the official CLI and try again.") from None
    if (not stat.S_ISREG(info.st_mode) or not os.access(real, os.X_OK) or info.st_uid not in (0, os.getuid())
            or info.st_mode & (stat.S_IWOTH)):
        raise UserFacingError("The provider CLI failed Scrooge's safety checks. Reinstall the official CLI and try again.")
    return str(real)


def locate(name: str, override_env: str) -> Optional[str]:
    """Known install locations first, then PATH. The result is resolved once to an absolute path."""
    override = os.environ.get(override_env)
    candidates: list[Path] = [Path(override)] if override else []
    candidates += [d / name for d in _candidate_dirs()]
    found = shutil.which(name)
    if found:
        candidates.append(Path(found))
    for candidate in candidates:
        try:
            if candidate.exists():
                return validate_executable(candidate)
        except UserFacingError:
            continue
    return None


class BoundedResult:
    def __init__(self, code: int, output: str):
        self.exit_code = code
        self.output = output


def _kill_tree(process: subprocess.Popen) -> None:
    if process.poll() is not None:
        return
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except (ProcessLookupError, PermissionError):
        return
    try:
        process.wait(timeout=3)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except (ProcessLookupError, PermissionError):
            pass
        try:
            process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            pass


def run_bounded(args: Sequence[str], env: dict[str, str], cwd: str, timeout: float, token: CancelToken,
                limit: int = OUTPUT_LIMIT, stdin_text: Optional[str] = None) -> BoundedResult:
    """Run without a shell. stdout is capped; stderr is discarded; the whole process group is
    terminated on cancellation, overflow, or deadline."""
    token.check()
    deadline = token.child(timeout)
    process = subprocess.Popen(list(args), cwd=cwd, env=env, stdin=subprocess.PIPE if stdin_text is not None else subprocess.DEVNULL,
                               stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, start_new_session=True)
    chunks: list[bytes] = []
    overflow = threading.Event()

    def reader():
        total = 0
        assert process.stdout is not None
        for block in iter(lambda: process.stdout.read(4096), b""):
            total += len(block)
            if total > limit:
                overflow.set()
                _kill_tree(process)
                return
            chunks.append(block)

    thread = threading.Thread(target=reader, daemon=True)
    thread.start()
    if stdin_text is not None and process.stdin:
        try:
            process.stdin.write(stdin_text.encode())
            process.stdin.close()
        except OSError:
            pass
    try:
        while process.poll() is None:
            if deadline.cancelled:
                _kill_tree(process)
                raise Cancelled("The provider CLI timed out or was canceled.")
            time.sleep(0.05)
        thread.join(timeout=5)
        if overflow.is_set():
            raise UserFacingError("Provider output exceeded the monitor's limit.")
        return BoundedResult(process.returncode, b"".join(chunks).decode("utf-8", errors="replace"))
    finally:
        _kill_tree(process)


# ---------------------------------------------------------------- data locations

def default_data_dir() -> Path:
    if sys.platform == "darwin":
        return Path.home() / "Library/Application Support/AI Token Scrooge"
    base = os.environ.get("XDG_DATA_HOME") or str(Path.home() / ".local/share")
    return Path(base) / "ai-token-scrooge"


DATA_DIR_ENV = "SCROOGE_DATA_DIR"


def resolve_data_dir(option: Optional[Path] = None, environ: Optional[dict] = None) -> tuple[Path, str]:
    """Choose the data folder: --data-dir, then $SCROOGE_DATA_DIR, then the platform default.
    Returns an absolute path and its source ("option", "environment" or "default"). An empty
    variable is ignored. Credentials stay under the platform default's Security folder, keyed by
    the chosen folder's identity, so moving data elsewhere never shares sign-ins between folders."""
    environ = os.environ if environ is None else environ
    if option is not None:
        return Path(option).expanduser().resolve(), "option"
    value = (environ.get(DATA_DIR_ENV) or "").strip()
    if value:
        return Path(value).expanduser().resolve(), "environment"
    return default_data_dir(), "default"


def ensure_private_dir(path: Path) -> Path:
    path.mkdir(parents=True, exist_ok=True, mode=0o700)
    info = path.lstat()
    if stat.S_ISLNK(info.st_mode) or not stat.S_ISDIR(info.st_mode):
        raise UserFacingError("Scrooge's private folder is redirected. Remove the link and try again.")
    if info.st_uid != os.getuid():
        raise UserFacingError("Scrooge's private folder belongs to another user.")
    if stat.S_IMODE(info.st_mode) != 0o700:
        os.chmod(path, 0o700)
    return path


def security_dir_for(data_dir: Path) -> Path:
    """Credential namespace keyed by the data directory's file identity, like the Windows build.
    Different names for the same directory share a namespace; separate data dirs stay isolated."""
    data_dir.mkdir(parents=True, exist_ok=True)
    info = data_dir.stat()
    digest = hashlib.sha256(f"{info.st_dev}:{info.st_ino}".encode()).hexdigest()[:24]
    root = ensure_private_dir(default_data_dir() / "Security")
    return ensure_private_dir(root / digest)


_CHILD = {"OpenAI": "Profiles", "Claude": "ClaudeProfiles", "Devin": "DevinProfiles"}


class ProfilePaths:
    """Uses persisted locations rather than probing folders or moving path-bound keyring profiles."""

    def __init__(self, data_dir: Path, security_dir: Optional[Path]):
        self.data_dir = Path(data_dir)
        self.security_dir = Path(security_dir) if security_dir else None

    def root_for(self, location: CredentialProfileLocation) -> Path:
        if location == CredentialProfileLocation.LegacyDataDirectory:
            return self.data_dir.resolve()
        if self.security_dir is None:
            raise RuntimeError("The local credential directory is not configured.")
        return self.security_dir.resolve()

    def path_for(self, provider: str, credential_id: str, location: CredentialProfileLocation) -> Path:
        if not GUID.match(credential_id or ""):
            raise ValueError("Invalid credential identity.")
        if provider not in _CHILD:
            raise ValueError("Unknown profile provider.")
        return self.root_for(location) / _CHILD[provider] / credential_id

    def ensure(self, provider: str, credential_id: str, location: CredentialProfileLocation) -> Path:
        path = self.path_for(provider, credential_id, location)
        ensure_private_dir(path.parent)
        return ensure_private_dir(path)

    def delete(self, provider: str, credential_id: str, location: CredentialProfileLocation) -> None:
        """Delete only the selected GUID-named profile after containment, ownership and link checks."""
        path = self.path_for(provider, credential_id, location)
        parent = path.parent
        try:
            info = path.lstat()
        except FileNotFoundError:
            return
        if stat.S_ISLNK(info.st_mode) or not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid():
            raise UserFacingError("Scrooge did not remove a redirected or foreign profile folder. Cleanup remains pending.")
        if os.path.realpath(path) != str(parent.resolve() / credential_id):
            raise UserFacingError("Scrooge did not remove a profile outside its own folder. Cleanup remains pending.")
        # shutil.rmtree unlinks nested symlinks without following them, so nothing outside the
        # validated tree is touched.

        def on_error(func, target, _exc):
            os.chmod(target, stat.S_IRWXU)
            func(target)

        if sys.version_info >= (3, 12):
            shutil.rmtree(path, onexc=on_error)
        else:  # pragma: no cover
            shutil.rmtree(path, onerror=on_error)


def first_line_version(text: str) -> Optional[tuple[int, ...]]:
    match = re.match(r"\s*v?(\d+)\.(\d+)\.(\d+)", text or "")
    return tuple(int(g) for g in match.groups()) if match else None
