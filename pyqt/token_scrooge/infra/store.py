"""Non-secret local state in SQLite (port of LocalStore). Tokens never belong here.

History is stored as runs: consecutive observations with identical measured state extend one
row (first/last timestamps, count, largest gap) rather than inventing intermediate usage.
"""
from __future__ import annotations

import json
import sqlite3
import threading
from contextlib import contextmanager
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Iterator, Optional, Sequence

from ..core import serialization as ser
from ..core.alerts import AlertEvent, prune_alerts, retention_cutoff
from ..core.model import CredentialProfileLocation, ProviderAccount, QuotaSnapshot, replace
from ..core.settings import RadarSettings
from .cli import GUID

SCHEMA_VERSION = 1
PROVIDERS = ("OpenAI", "Claude", "GitHub Copilot", "Devin")


@dataclass(frozen=True)
class PendingCredential:
    Provider: str
    CredentialId: str
    ProfileLocation: CredentialProfileLocation = CredentialProfileLocation.LegacyDataDirectory


@dataclass
class SavedMonitoringState:
    accounts: list[ProviderAccount]
    snapshots: list[QuotaSnapshot]
    retired: list[ProviderAccount]
    alerts: list[AlertEvent]
    snoozed: dict[str, datetime] = field(default_factory=dict)


def _ms(value: datetime) -> int:
    return int(value.timestamp() * 1000)


def _from_ms(value: int) -> datetime:
    return datetime.fromtimestamp(value / 1000, tz=timezone.utc)


def _validate_pending(item: PendingCredential) -> PendingCredential:
    if item.Provider not in PROVIDERS or not GUID.match(item.CredentialId or ""):
        raise ValueError("Invalid pending credential record.")
    return item


class LocalStore:
    def __init__(self, path: Path, clock=None):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._lock = threading.RLock()
        self._clock = clock or (lambda: datetime.now(timezone.utc))
        self._retention = 90
        with self._connect() as db:
            version = db.execute("PRAGMA user_version").fetchone()[0]
            if version > SCHEMA_VERSION:
                raise RuntimeError("This database was created by a newer Scrooge version. Update Scrooge before opening it.")
            db.execute("CREATE TABLE IF NOT EXISTS state(key TEXT PRIMARY KEY, value TEXT NOT NULL)")
            db.execute("""CREATE TABLE IF NOT EXISTS history(
                    account_id TEXT NOT NULL, observed_at INTEGER NOT NULL, last_at INTEGER NOT NULL,
                    count INTEGER NOT NULL DEFAULT 1, max_gap_ms INTEGER NOT NULL DEFAULT 0,
                    signature TEXT NOT NULL, value TEXT NOT NULL, PRIMARY KEY(account_id, observed_at))""")
            db.execute("CREATE INDEX IF NOT EXISTS history_last ON history(account_id, last_at)")
            db.execute(f"PRAGMA user_version={SCHEMA_VERSION}")
        try:
            self.path.chmod(0o600)
        except OSError:
            pass
        settings = self.read("settings", RadarSettings)
        self._retention = settings.retention_days if settings else 90
        self.prune()

    @contextmanager
    def _connect(self) -> Iterator[sqlite3.Connection]:
        with self._lock:
            db = sqlite3.connect(self.path, isolation_level=None, timeout=10)
            try:
                db.execute("BEGIN IMMEDIATE")
                yield db
                db.execute("COMMIT")
            except BaseException:
                if db.in_transaction:
                    db.execute("ROLLBACK")
                raise
            finally:
                db.close()

    # ------------------------------------------------------------- key/value
    @staticmethod
    def _get(db: sqlite3.Connection, key: str) -> Optional[str]:
        row = db.execute("SELECT value FROM state WHERE key=?", (key,)).fetchone()
        return row[0] if row else None

    @staticmethod
    def _put(db: sqlite3.Connection, key: str, value: Any) -> None:
        db.execute("INSERT INTO state(key,value) VALUES(?,?) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
                   (key, ser.dumps(value)))

    def read(self, key: str, tp: Any) -> Any:
        with self._connect() as db:
            text = self._get(db, key)
        return ser.loads(tp, text) if text is not None else None

    def write(self, key: str, value: Any) -> None:
        if key == "alerts":
            value = prune_alerts(value, self._clock(), self._retention)
        with self._connect() as db:
            self._put(db, key, value)
        if key == "settings" and isinstance(value, RadarSettings):
            self._retention = value.retention_days
            self.prune()

    # ------------------------------------------------------------- monitoring state
    def load_monitoring_state(self) -> SavedMonitoringState:
        with self._connect() as db:
            def read(key, tp, fallback):
                text = self._get(db, key)
                return ser.loads(tp, text) if text is not None else fallback
            accounts = read("accounts", list[ProviderAccount], [])
            snapshots = read("snapshots", list[QuotaSnapshot], [])
            retired = read("retired", list[ProviderAccount], [])
            alerts = read("alerts", list[AlertEvent], [])
            snoozed = read("snoozed", dict[str, datetime], {})
            pending = [_validate_pending(p) for p in read("pending-credentials", list[PendingCredential], [])]
        active = {a.credential for a in accounts}
        # Abandoned sign-ins become retirement work so cleanup can retry across restarts.
        abandoned = [ProviderAccount(p.CredentialId, p.Provider, "", "", "", Enabled=False, CredentialId=p.CredentialId,
                                     ProfileLocation=p.ProfileLocation) for p in pending if p.CredentialId not in active]
        unique: dict[str, ProviderAccount] = {}
        for item in retired + abandoned:
            unique.setdefault(item.credential, item)
        return SavedMonitoringState(accounts, snapshots, list(unique.values()), alerts, snoozed)

    def write_monitoring_state(self, accounts: Sequence[ProviderAccount], snapshots: Sequence[QuotaSnapshot],
                               retired: Optional[Sequence[ProviderAccount]] = None,
                               alerts: Optional[Sequence[AlertEvent]] = None,
                               history_snapshot: Optional[QuotaSnapshot] = None) -> list[AlertEvent]:
        """Commit accounts, snapshots, new alert intents and a history sample in one transaction."""
        owner = None
        if history_snapshot is not None:
            owner = next((a for a in accounts if history_snapshot.belongs_to(a)), None)
            if history_snapshot not in snapshots or owner is None:
                raise ValueError("The history snapshot must belong to an accepted account and its current monitoring state.")
        inserted: list[AlertEvent] = []
        with self._connect() as db:
            self._put(db, "accounts", list(accounts))
            self._put(db, "snapshots", list(snapshots))
            if retired is not None:
                self._put(db, "retired", list(retired))
            pending = self._read_pending(db)
            accepted = {a.credential for a in accounts}
            for p in pending:
                match = next((a for a in accounts if a.credential == p.CredentialId), None)
                if match is not None and (match.Provider != p.Provider or match.ProfileLocation != p.ProfileLocation):
                    raise ValueError("The accepted credential does not match its pending provider and location.")
            if any(p.CredentialId in accepted for p in pending):
                self._put(db, "pending-credentials", [p for p in pending if p.CredentialId not in accepted])
            if alerts:
                events = self._read_alerts(db)
                for alert in alerts:
                    if self._add_alert(events, alert):
                        inserted.append(events[-1])
                if inserted:
                    self._put(db, "alerts", events)
            if history_snapshot is not None:
                self._write_history(db, history_snapshot, owner.effective_polling_minutes)
        return inserted

    # ------------------------------------------------------------- credential journal
    @staticmethod
    def _read_pending(db) -> list[PendingCredential]:
        text = LocalStore._get(db, "pending-credentials")
        return [_validate_pending(p) for p in ser.loads(list[PendingCredential], text)] if text else []

    def stage_credential(self, provider: str, credential_id: str,
                         location: CredentialProfileLocation = CredentialProfileLocation.LocalSecurityDirectory) -> None:
        """Journal a sign-in before the CLI or secret store can create credentials."""
        item = _validate_pending(PendingCredential(provider, credential_id, location))
        with self._connect() as db:
            text = self._get(db, "accounts")
            accounts = ser.loads(list[ProviderAccount], text) if text else []
            if any(a.credential == credential_id for a in accounts):
                raise RuntimeError("A new sign-in cannot reuse an active credential.")
            pending = self._read_pending(db)
            if any(p.CredentialId == credential_id and (p.Provider != provider or p.ProfileLocation != location) for p in pending):
                raise ValueError("Pending credential provider or location changed.")
            self._put(db, "pending-credentials", [p for p in pending if p.CredentialId != credential_id] + [item])

    def complete_credential_cleanup(self, credential_id: str) -> None:
        if not GUID.match(credential_id or ""):
            raise ValueError("Invalid credential identity.")
        with self._connect() as db:
            self._put(db, "pending-credentials", [p for p in self._read_pending(db) if p.CredentialId != credential_id])

    # ------------------------------------------------------------- alerts
    @staticmethod
    def _read_alerts(db) -> list[AlertEvent]:
        text = LocalStore._get(db, "alerts")
        return ser.loads(list[AlertEvent], text) if text else []

    def _add_alert(self, events: list[AlertEvent], alert: AlertEvent) -> bool:
        if alert.CreatedAt < retention_cutoff(self._clock(), self._retention) or any(e.Key == alert.Key for e in events):
            return False
        events.append(replace(alert, Delivered=False))
        return True

    def record_alert(self, alert: AlertEvent) -> bool:
        with self._connect() as db:
            events = self._read_alerts(db)
            if not self._add_alert(events, alert):
                return False
            self._put(db, "alerts", events)
            return True

    def record_alert_delivery(self, key: str, delivered: bool) -> None:
        if not delivered:
            return
        with self._connect() as db:
            events = self._read_alerts(db)
            for i, e in enumerate(events):
                if e.Key == key and not e.Delivered:
                    events[i] = replace(e, Delivered=True)
                    self._put(db, "alerts", events)
                    return

    # ------------------------------------------------------------- history
    @staticmethod
    def _signature(snapshot: QuotaSnapshot) -> str:
        plain = ser.to_plain(replace(snapshot, ObservedAt=datetime.fromtimestamp(0, timezone.utc), ProviderDataAt=None))
        return json.dumps(plain, sort_keys=True, separators=(",", ":"))

    def _write_history(self, db, snapshot: QuotaSnapshot, polling_minutes: int) -> None:
        at = _ms(snapshot.ObservedAt)
        signature = self._signature(snapshot)
        last = db.execute("SELECT observed_at,last_at,max_gap_ms,signature FROM history WHERE account_id=? "
                          "ORDER BY last_at DESC, observed_at DESC LIMIT 1", (snapshot.AccountId,)).fetchone()
        if last and last[3] == signature and at > last[1]:
            db.execute("UPDATE history SET last_at=?, count=count+1, max_gap_ms=? WHERE account_id=? AND observed_at=?",
                       (at, max(last[2], at - last[1]), snapshot.AccountId, last[0]))
        elif not last or at > last[1]:
            db.execute("INSERT OR IGNORE INTO history(account_id,observed_at,last_at,count,max_gap_ms,signature,value) "
                       "VALUES(?,?,?,?,?,?,?)", (snapshot.AccountId, at, at, 1, 0, signature, ser.dumps(snapshot)))
        self._prune_history(db)

    def record_snapshot(self, snapshot: QuotaSnapshot, polling_minutes: int = 2) -> None:
        with self._connect() as db:
            self._write_history(db, snapshot, polling_minutes)

    def history(self, account_id: str, start: datetime, end: datetime, include_preceding: bool = True) -> list[QuotaSnapshot]:
        """Measured run endpoints in chronological order, plus the nearest preceding anchor."""
        with self._connect() as db:
            rows = db.execute("SELECT observed_at,last_at,max_gap_ms,value FROM history WHERE account_id=? "
                              "AND observed_at<=? AND last_at>=? ORDER BY observed_at",
                              (account_id, _ms(end), _ms(start))).fetchall()
            if include_preceding:
                before = db.execute("SELECT observed_at,last_at,max_gap_ms,value FROM history WHERE account_id=? "
                                    "AND last_at<? ORDER BY last_at DESC LIMIT 1", (account_id, _ms(start))).fetchone()
                if before:
                    rows.insert(0, before)
        result: list[QuotaSnapshot] = []
        for first_ms, last_ms, gap, value in rows:
            try:
                first = ser.loads(QuotaSnapshot, value)
            except (ValueError, KeyError, TypeError):
                continue
            if first.AccountId != account_id:
                continue
            result.append(first)
            if last_ms > first_ms:
                result.append(replace(first, ObservedAt=_from_ms(last_ms), HistoryContinuousFrom=first.ObservedAt,
                                      HistoryMaximumGapSeconds=gap / 1000))
        return result

    def delete_history(self, account_id: str) -> None:
        with self._connect() as db:
            db.execute("DELETE FROM history WHERE account_id=?", (account_id,))

    def _prune_history(self, db) -> None:
        db.execute("DELETE FROM history WHERE last_at < ?", (_ms(retention_cutoff(self._clock(), self._retention)),))

    def prune(self) -> None:
        with self._connect() as db:
            self._prune_history(db)
            alerts = self._read_alerts(db)
            kept = prune_alerts(alerts, self._clock(), self._retention)
            if len(kept) != len(alerts):
                self._put(db, "alerts", kept)
