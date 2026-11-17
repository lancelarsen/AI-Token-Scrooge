"""Orchestration (port of the MainPage partials: Refresh, Accounts, Preferences, Alerts, Snooze).

Rules preserved from the Windows app:
- Provider reads run off the UI thread, each with its own 45-second deadline; a trigger that arrives
  while an account is refreshing reuses the active read.
- A finished read is applied only if the account object is still the current generation.
- A successful read commits snapshot, history sample and alert intents together before display.
- Account changes are persisted before credentials are retired; cleanup stays retryable.
- Settings are persisted before the in-memory copy is adopted.
"""
from __future__ import annotations

import threading
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Callable, Optional

from PyQt6.QtCore import QObject, QTimer, pyqtSignal

from ..core.alerts import (AlertEvaluator, AlertEvent, MonitorStatus, failure_backoff, icon_alert, next_refresh_after_success,
                           prune_alerts, retention_cutoff)
from ..core.labels import resolve_zone
from ..core.model import (AccountRefreshLease, ProviderAccount, QuotaSnapshot, SourceHealth, replace)
from ..core.settings import AccountSortMode, AlertThresholds, RadarSettings, move_in_order, normalize_order
from ..infra.errors import CancelToken, ProviderAccessError, safe_message
from ..infra.monitor import AccountMonitor
from ..infra.store import LocalStore
from . import tasks
from .presentation import DashboardState, HistorySeries, build_history, demo_history

READ_DEADLINE = 45.0
CLEANUP_DEADLINE = 90.0


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


class AppController(QObject):
    changed = pyqtSignal()               # Accounts, snapshots or settings changed: re-render.
    clock = pyqtSignal()                 # Timestamp-only update (countdowns).
    alert = pyqtSignal(object)           # AlertEvent to show as an in-app banner.
    status = pyqtSignal(str)             # Header freshness / problem text.
    refreshing_changed = pyqtSignal(bool)

    def __init__(self, store: LocalStore, monitor: AccountMonitor, data_dir: Path, notifier=None, sounds=None):
        super().__init__()
        self.store, self.monitor, self.data_dir = store, monitor, data_dir
        self.notifier, self.sounds = notifier, sounds
        self.state = DashboardState()
        self.alert_history: list[AlertEvent] = []
        self.snoozed: dict[str, datetime] = {}
        self.retired: list[ProviderAccount] = []
        self.next_refresh: dict[str, datetime] = {}
        self.throttled_until: dict[str, datetime] = {}
        self.failures: dict[str, int] = {}
        self.pending: dict[str, tuple[CancelToken, list[Callable[[], None]]]] = {}
        self.loaded = False
        self.exiting = False
        self.problem: Optional[str] = None
        self._cleanup_running = False
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._tick)
        self._clock_timer = QTimer(self)
        self._clock_timer.timeout.connect(self.clock.emit)

    # ------------------------------------------------------------------ properties
    @property
    def settings(self) -> RadarSettings:
        return self.state.settings

    @property
    def zone(self):
        return resolve_zone(self.settings.TimeZoneId)

    @property
    def is_refreshing(self) -> bool:
        return bool(self.pending)

    def monitor_status(self) -> MonitorStatus:
        return MonitorStatus.evaluate(self.state.accounts, self.state.snapshots, self.settings)

    def icon_alert(self) -> Optional[AlertEvent]:
        return icon_alert(self.state.accounts, self.state.snapshots, self.settings, utcnow())

    # ------------------------------------------------------------------ lifecycle
    def start(self, demo: bool = False) -> None:
        settings = self.store.read("settings", RadarSettings) or RadarSettings()
        self.state.settings = settings
        if demo:
            self.state.load_demo(utcnow())
        else:
            saved = self.store.load_monitoring_state()
            self.state.accounts = saved.accounts
            # Saved values stay visible but are not presented as freshly confirmed.
            self.state.snapshots = {s.AccountId: replace(s, Health=SourceHealth.Stale, Message="Saved values — refreshing")
                                    for s in saved.snapshots if any(s.belongs_to(a) for a in saved.accounts)}
            self.retired = saved.retired
            self.alert_history = prune_alerts(saved.alerts, utcnow(), settings.retention_days)
            self.snoozed = saved.snoozed
        self.loaded = True
        self.changed.emit()
        self._timer.start(20_000)
        self._clock_timer.start(30_000)
        if not demo:
            self.refresh(force=True)

    def shutdown(self) -> None:
        self.exiting = True
        self._timer.stop()
        self._clock_timer.stop()
        for token, _ in list(self.pending.values()):
            token.cancel()

    def _tick(self) -> None:
        self.refresh(force=False)
        self.clock.emit()

    # ------------------------------------------------------------------ refresh
    def refresh(self, force: bool = True, done: Optional[Callable[[], None]] = None) -> None:
        """Startup, polling, manual refresh and restore all use this path."""
        if not self.loaded or self.exiting:
            if done:
                done()
            return
        if self.state.is_demo:
            self.changed.emit()
            if done:
                done()
            return
        now = utcnow()
        waiting: list[str] = []
        for account in [a for a in self.state.accounts if a.Enabled]:
            if account.Id in self.pending:
                if force:
                    waiting.append(account.Id)
                continue
            if self.throttled_until.get(account.Id, now) > now:
                continue  # Even a manual refresh honors a provider's retry deadline.
            if not force and self.next_refresh.get(account.Id, now) > now:
                continue
            self._refresh_account(account)
            waiting.append(account.Id)
        if done:
            if not waiting:
                done()
            else:
                remaining = set(waiting)

                def finished(account_id: str):
                    remaining.discard(account_id)
                    if not remaining:
                        done()
                for account_id in waiting:
                    self.pending[account_id][1].append(lambda a=account_id: finished(a))
        if not waiting:
            self.changed.emit()
        self._cleanup_retired()

    def _refresh_account(self, account: ProviderAccount) -> None:
        token = CancelToken(READ_DEADLINE)
        self.pending[account.Id] = (token, [])
        self.refreshing_changed.emit(True)
        lease = AccountRefreshLease(account)

        def read():
            return self.monitor.refresh(account, token)

        def complete(result: object) -> None:
            callbacks = self.pending.pop(account.Id, (None, []))[1]
            try:
                if not self.exiting and lease.is_current(self.state.account(account.Id)):
                    self._apply_reading(account, result)
            finally:
                for cb in callbacks:
                    cb()
                self.refreshing_changed.emit(self.is_refreshing)

        tasks.run(read, done=complete, failed=complete)

    def _apply_reading(self, account: ProviderAccount, result: object) -> None:
        now = utcnow()
        previous = self.state.snapshots.get(account.Id)
        reading = result if isinstance(result, QuotaSnapshot) else None
        if reading is None:
            error = result if isinstance(result, BaseException) else RuntimeError("unknown")
            count = self.failures.get(account.Id, 0) + 1
            self.failures[account.Id] = count
            if isinstance(error, ProviderAccessError) and error.retry_at is not None:
                self.next_refresh[account.Id] = self.throttled_until[account.Id] = error.retry_at
            else:
                self.next_refresh[account.Id] = now + failure_backoff(count)
            last = previous or QuotaSnapshot(account.Id, account.Subject, account.Scope, now, (), None)
            # Keep the last valid values without presenting them as freshly confirmed.
            snapshot = replace(last, Health=error.health if isinstance(error, ProviderAccessError) else SourceHealth.Offline,
                               Message=safe_message(error))
        else:
            self.failures[account.Id] = 0
            self.next_refresh[account.Id] = next_refresh_after_success(reading, account, now)
            snapshot = reading
        alerts = AlertEvaluator.evaluate(account, previous, reading, self.settings, now) if reading else []
        snapshots = [s for s in self.state.snapshots.values() if s.AccountId != account.Id] + [snapshot]
        recorded: Optional[list[AlertEvent]] = None
        try:
            recorded = self.store.write_monitoring_state(self.state.accounts, snapshots, alerts=alerts,
                                                         history_snapshot=reading)
        except Exception:  # noqa: BLE001 - surface as stale state, retry next read
            last = previous or QuotaSnapshot(account.Id, account.Subject, account.Scope, now, (), None)
            snapshot = replace(last, Health=SourceHealth.Stale,
                               Message="Local history could not be saved. Check available space and folder access.")
        self.state.snapshots[account.Id] = snapshot
        self.changed.emit()
        if reading is not None and recorded is not None:
            self._deliver(account, recorded)

    # ------------------------------------------------------------------ alerts
    def _deliver(self, account: ProviderAccount, recorded: list[AlertEvent]) -> None:
        now = utcnow()
        lease = AccountRefreshLease(account)
        for alert in recorded:
            if not lease.is_current(self.state.account(account.Id)):
                return
            if not AlertEvaluator.is_enabled(alert, self.settings, self.state.snapshots.get(account.Id)):
                continue
            self.alert_history = [e for e in self.alert_history if e.Key != alert.Key] + [alert]
            until = self.snoozed.get(account.Id)
            can_deliver = (self.settings.Notifications and not AlertEvaluator.is_quiet(self.settings, now, self.zone)
                           and (until is None or until <= now))
            sent = False
            if can_deliver:
                sent = bool(self.notifier and self.notifier.show(alert, account.Provider))
                self.alert.emit(alert)
                self.play("alert")
            if sent:
                try:
                    self.store.record_alert_delivery(alert.Key, True)
                    self.alert_history = [replace(e, Delivered=True) if e.Key == alert.Key else e for e in self.alert_history]
                except Exception:  # noqa: BLE001
                    self.status.emit("Delivery status could not be saved for one or more alerts.")
        self.alert_history = prune_alerts(self.alert_history, now, self.settings.retention_days)

    def play(self, action: str, preview: bool = False) -> None:
        if self.sounds:
            self.sounds.play(self.settings, action, preview)

    def snooze(self, account_id: str) -> None:
        until = utcnow() + timedelta(hours=1)
        updated = dict(self.snoozed)
        updated[account_id] = until
        if not self.state.is_demo:
            self.store.write("snoozed", updated)  # Persist before suppressing.
        self.snoozed = updated

    def test_notification(self) -> AlertEvent:
        account_id = self.state.accounts[0].Id if self.state.accounts else "test"
        alert = AlertEvent("test/" + uuid.uuid4().hex, account_id, None, "Scrooge Notification Test",
                           "This is a test. Your account usage has not changed.", utcnow())
        if not self.state.is_demo:
            self.store.record_alert(alert)
        self.alert_history.append(alert)
        provider = self.state.account(account_id).Provider if self.state.account(account_id) else "Scrooge"
        sent = bool(self.notifier and self.notifier.show(alert, provider, force=True))
        if sent:
            if not self.state.is_demo:
                self.store.record_alert_delivery(alert.Key, True)
            self.alert_history[-1] = replace(alert, Delivered=True)
        self.alert.emit(alert)
        self.play("alert")
        return alert

    # ------------------------------------------------------------------ preferences
    def save_settings(self, updated: RadarSettings) -> None:
        """Persist first; adopt only on success so a failure cannot be concealed."""
        if not updated.has_valid_thresholds:
            raise ValueError("Critical must be below Warning; Info must be at or above Warning.")
        if not self.state.is_demo:
            self.store.write("settings", updated)
        self.state.settings = updated
        self.changed.emit()

    def set_capacity_alerts(self, account_id: str, window_id: str, enabled: bool) -> None:
        self.save_settings(self.settings.with_capacity_alerts(account_id, window_id, enabled))

    def save_thresholds(self, account_id: str, window_id: str, values: AlertThresholds) -> None:
        self.save_settings(self.settings.with_window_thresholds(account_id, window_id, values.Warning, values.Critical, values.Info))

    def set_sort(self, mode: AccountSortMode) -> None:
        self.save_settings(replace(self.settings, AccountSort=mode))

    def move_account(self, account_id: str, index: int) -> None:
        order = normalize_order(self.settings.AccountOrder, [a.Id for a in self.state.accounts])
        self.save_settings(replace(self.settings, AccountOrder=tuple(move_in_order(order, account_id, index))))

    def set_timeline_account(self, account_id: str, visible: bool) -> None:
        self.save_settings(self.settings.with_timeline_account(account_id, visible))

    def set_history_enabled(self, enabled: bool) -> None:
        self.save_settings(replace(self.settings, HistoryEnabled=enabled))

    # ------------------------------------------------------------------ accounts
    def _persist_accounts(self, accounts: list[ProviderAccount], snapshots: dict[str, QuotaSnapshot],
                          retired: list[ProviderAccount]) -> None:
        if not self.state.is_demo:
            self.store.write_monitoring_state(accounts, list(snapshots.values()), retired=retired)
        self.state.accounts, self.state.snapshots, self.retired = accounts, snapshots, retired

    def update_account(self, edited: ProviderAccount) -> None:
        """Rename, recolor, change polling, or pause monitoring. Identity is never edited here."""
        current = self.state.account(edited.Id)
        if current is None:
            return
        updated = replace(current, Nickname=edited.Nickname.strip() or current.Nickname, Enabled=edited.Enabled,
                          PollingMinutes=edited.effective_polling_minutes, LineColor=edited.LineColor)
        accounts = [updated if a.Id == updated.Id else a for a in self.state.accounts]
        self._persist_accounts(accounts, dict(self.state.snapshots), list(self.retired))
        if updated.Enabled and not current.Enabled:
            self.next_refresh.pop(updated.Id, None)
            self.refresh(force=False)
        self.changed.emit()

    def accept_account(self, account: ProviderAccount, snapshot: QuotaSnapshot) -> None:
        """Commit a new or reconnected account, then retire the replaced credential."""
        if self.state.is_demo:
            self.state.clear_demo()
        previous = self.state.account(account.Id)
        accounts = [account if a.Id == account.Id else a for a in self.state.accounts]
        if previous is None:
            accounts.append(account)
        retired = list(self.retired)
        if previous is not None and previous.credential != account.credential:
            retired.append(previous)
        snapshots = dict(self.state.snapshots)
        snapshots[account.Id] = snapshot
        self.store.write_monitoring_state(accounts, list(snapshots.values()), retired=retired,
                                          history_snapshot=snapshot if snapshot.Health == SourceHealth.Healthy else None)
        self.state.accounts, self.state.snapshots, self.retired = accounts, snapshots, retired
        self.next_refresh[account.Id] = next_refresh_after_success(snapshot, account, utcnow())
        self.failures.pop(account.Id, None)
        self.throttled_until.pop(account.Id, None)
        self.changed.emit()
        self._cleanup_retired()

    def remove_account(self, account_id: str) -> None:
        account = self.state.account(account_id)
        if account is None:
            return
        if account_id in self.pending:
            self.pending[account_id][0].cancel()
        accounts = [a for a in self.state.accounts if a.Id != account_id]
        snapshots = {k: v for k, v in self.state.snapshots.items() if k != account_id}
        retired = list(self.retired) + ([account] if not self.state.is_demo else [])
        self._persist_accounts(accounts, snapshots, retired)
        settings = replace(self.settings,
                           AccountOrder=tuple(i for i in self.settings.AccountOrder or () if i != account_id),
                           HiddenTimelineAccounts=tuple(i for i in self.settings.HiddenTimelineAccounts or () if i != account_id))
        try:
            self.save_settings(settings)
        except Exception:  # noqa: BLE001 - stale order entries are normalized away on load
            pass
        if not self.state.is_demo:
            try:
                self.store.delete_history(account_id)
            except Exception:  # noqa: BLE001
                pass
        self.changed.emit()
        self._cleanup_retired()

    def _cleanup_retired(self) -> None:
        """Disconnect only Scrooge-owned credentials; failures remain queued for a later retry."""
        if self._cleanup_running or not self.retired or self.state.is_demo or self.exiting:
            return
        self._cleanup_running = True
        queue = list(self.retired)

        def work():
            done = []
            for account in queue:
                try:
                    self.monitor.disconnect(account, CancelToken(CLEANUP_DEADLINE))
                    done.append(account.credential)
                except Exception:  # noqa: BLE001 - keep retryable
                    pass
            return done

        def finished(result):
            self._cleanup_running = False
            if isinstance(result, BaseException) or not result:
                return
            remaining = [a for a in self.retired if a.credential not in result]
            try:
                self.store.write_monitoring_state(self.state.accounts, list(self.state.snapshots.values()), retired=remaining)
                self.retired = remaining
                self.changed.emit()
            except Exception:  # noqa: BLE001
                pass

        tasks.run(work, done=finished, failed=finished)

    # ------------------------------------------------------------------ demo
    def load_demo(self) -> None:
        self.state.load_demo(utcnow())
        self.changed.emit()

    def exit_demo(self) -> None:
        self.state.clear_demo()
        saved = self.store.load_monitoring_state()
        self.state.accounts = saved.accounts
        self.state.snapshots = {s.AccountId: s for s in saved.snapshots}
        self.state.settings = self.store.read("settings", RadarSettings) or RadarSettings()
        self.changed.emit()
        self.refresh(force=True)

    # ------------------------------------------------------------------ history
    def load_history(self, done: Callable[[list[HistorySeries]], None]) -> None:
        """Build per-account history series off the UI thread."""
        accounts = list(self.state.accounts)
        demo = self.state.is_demo
        now = utcnow()
        start = now - timedelta(days=max(8, min(self.settings.retention_days, 120)))
        demo_samples = demo_history(self.state, now) if demo else {}

        def work():
            series = []
            for account in accounts:
                samples = demo_samples.get(account.Id, []) if demo else self.store.history(account.Id, start, now)
                # Demo samples are hourly-ish; give them a matching gap allowance so they draw as recorded lines.
                series.append(build_history(account, samples, now - timedelta(days=7) if demo else start, now,
                                            timedelta(hours=1) if demo else None))
            return series

        tasks.run(work, done=done, failed=lambda e: done([]))
