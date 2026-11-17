"""Alert rules, monitoring status, refresh scheduling and retention (port of Alerts.cs and friends)."""
from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta, tzinfo
from typing import Iterable, Mapping, Optional, Sequence

from .labels import format_reset, resolve_zone
from .model import (CapacityState, ProviderAccount, QuotaSnapshot, QuotaWindow, ResetTime, SourceHealth,
                    fmt_whole, round_away)
from .settings import RadarSettings, ReminderSeverity, TimedReminder


@dataclass(frozen=True)
class AlertEvent:
    Key: str
    AccountId: str
    WindowId: Optional[str]
    Title: str
    Detail: str
    CreatedAt: datetime
    Delivered: bool = False
    Severity: Optional[ReminderSeverity] = None
    ReminderId: Optional[str] = None
    IsRefreshReminder: bool = False


_CAPACITY_SUFFIXES = ("/info", "/low", "/critical", "/exhausted")


def _fmt_left(value: Optional[float]) -> str:
    if value is None:
        return ""
    rounded = round_away(value, 1)
    return f"{rounded:.0f}" if rounded == int(rounded) else f"{rounded:.1f}"


class AlertEvaluator:
    @staticmethod
    def is_enabled(alert: AlertEvent, settings: RadarSettings, snapshot: Optional[QuotaSnapshot] = None) -> bool:
        if alert.WindowId is not None and not settings.capacity_alerts_enabled(alert.AccountId, alert.WindowId):
            return False
        if (alert.ReminderId is not None and not alert.IsRefreshReminder and snapshot is not None and snapshot.Windows
                and not any(settings.capacity_alerts_enabled(alert.AccountId, w.Id) for w in snapshot.Windows)):
            return False
        if alert.ReminderId is not None:
            reminders = settings.effective_refresh_reminders if alert.IsRefreshReminder else settings.effective_reset_reminders
            return ((alert.IsRefreshReminder or settings.rule_for(alert.AccountId).Expiry)
                    and any(r.Id == alert.ReminderId and r.is_valid for r in reminders))
        return (alert.WindowId is None or not alert.Key.endswith(_CAPACITY_SUFFIXES)
                or settings.capacity_alerts_enabled(alert.AccountId, alert.WindowId))

    @staticmethod
    def is_quiet(settings: RadarSettings, now: datetime, zone: tzinfo) -> bool:
        if not settings.QuietHours:
            return False
        hour = now.astimezone(zone).hour
        start, end = settings.QuietStartHour, settings.QuietEndHour
        return start <= hour < end if start <= end else hour >= start or hour < end

    @staticmethod
    def evaluate(account: ProviderAccount, previous: Optional[QuotaSnapshot], current: QuotaSnapshot,
                 settings: RadarSettings, now: datetime) -> list[AlertEvent]:
        if current.Health != SourceHealth.Healthy:
            return []
        alerts: list[AlertEvent] = []
        for window in current.Windows:
            if not settings.capacity_alerts_enabled(account.Id, window.Id):
                continue
            before = next((w for w in previous.Windows if w.Id == window.Id), None) if previous else None
            if window.ResetsAt is not None:
                reminder = TimedReminder.due(settings.effective_refresh_reminders, window.ResetsAt, now)
                if reminder and not _is_moving_estimate(before.ResetsAt if before else None, window.ResetsAt,
                                                        previous.ObservedAt if previous else None, reminder, settings, now):
                    alerts.append(_reminder(account, window.Id, window.Id, window.ResetsAt, reminder, True, settings, now, window.Name))
            rule = settings.rule_for(account.Id, window.Id)
            state = window.capacity(rule.LowThreshold, rule.CriticalThreshold, rule.InfoThreshold)
            period = str(int(window.ResetsAt.timestamp())) if window.ResetsAt else "unknown"
            threshold = {CapacityState.Exhausted: 0, CapacityState.Critical: rule.CriticalThreshold,
                         CapacityState.Info: rule.InfoThreshold}.get(state, rule.LowThreshold)
            old_left = before.remaining_percent if before else None
            crossed = old_left is None or before.ResetsAt != window.ResetsAt or old_left > threshold
            enabled = {CapacityState.Info: rule.Info, CapacityState.Low: rule.Low, CapacityState.Critical: rule.Critical,
                       CapacityState.Exhausted: rule.Exhausted}.get(state, False)
            if crossed and enabled:
                severity = state.name.lower()
                verb = "is exhausted" if state == CapacityState.Exhausted else "Info Alert" if state == CapacityState.Info else "is running low"
                level = (ReminderSeverity.Info if state == CapacityState.Info else
                         ReminderSeverity.Warning if state == CapacityState.Low else ReminderSeverity.Critical)
                alerts.append(AlertEvent(f"{account.Id}/{window.Id}/{period}/{severity}", account.Id, window.Id,
                                         f"{window.Name} {verb}",
                                         f"{_fmt_left(window.remaining_percent)}% left · {account.Provider} / {account.Nickname}",
                                         now, Severity=level))
            if (rule.Restored and before is not None and before.capacity() == CapacityState.Exhausted
                    and (window.remaining_percent or 0) > 0 and current.UsageAllowed is True):
                alerts.append(AlertEvent(f"{account.Id}/{window.Id}/{period}/restored", account.Id, window.Id,
                                         f"{window.Name} is available again",
                                         f"Provider confirmed capacity · {account.Provider} / {account.Nickname}", now))
        reserve = current.Reserve
        if settings.rule_for(account.Id).Expiry and reserve is not None and reserve.AvailableCount != 0:
            for credit in reserve.Credits or ():
                if credit.Status != "available" or credit.ExpiresAt is None or credit.ExpiresAt <= now:
                    continue
                reminder = TimedReminder.due(settings.effective_reset_reminders, credit.ExpiresAt, now)
                if reminder:
                    alerts.append(_reminder(account, None, credit.Id, credit.ExpiresAt, reminder, False, settings, now, "Earned Reset"))
        return [a for a in alerts if AlertEvaluator.is_enabled(a, settings, current)]


def _is_moving_estimate(before: Optional[datetime], at: datetime, observed: Optional[datetime], reminder: TimedReminder,
                        settings: RadarSettings, now: datetime) -> bool:
    """Some providers recompute a rolling reset estimate on each read; don't repeat a milestone for a few minutes' drift."""
    if before is None or before == at or before <= now or observed is None:
        return False
    if abs((at - before).total_seconds()) > 600:
        return False
    prior = TimedReminder.due(settings.effective_refresh_reminders, before, observed)
    return prior is not None and prior.Id == reminder.Id


def _reminder(account: ProviderAccount, window: Optional[str], target: str, at: datetime, reminder: TimedReminder,
              refresh: bool, settings: RadarSettings, now: datetime, name: str) -> AlertEvent:
    kind = "Quota Refresh" if refresh else "Reset Expiration"
    return AlertEvent(
        f"{account.Id}/{'refresh' if refresh else 'credit'}/{target}/{int(at.timestamp())}/{reminder.Id}",
        account.Id, window, f"{reminder.Severity.name} Alert · {kind}",
        f"{name} · {ResetTime.countdown(at, now)} · {format_reset(at, resolve_zone(settings.TimeZoneId))} · {account.Nickname}",
        now, Severity=reminder.Severity, ReminderId=reminder.Id, IsRefreshReminder=refresh)


def icon_alert(accounts: Iterable[ProviderAccount], snapshots: Mapping[str, QuotaSnapshot], settings: RadarSettings,
               now: datetime) -> Optional[AlertEvent]:
    """A persistent status indicator, independent of popup delivery and quiet hours."""
    if not settings.AlertOnIcon:
        return None
    found: list[AlertEvent] = []
    for a in accounts:
        s = snapshots.get(a.Id)
        if a.Enabled and s is not None and s.belongs_to(a):
            found += [e for e in AlertEvaluator.evaluate(a, None, s, settings, now)
                      if e.Severity is not None and AlertEvaluator.is_enabled(e, settings)]
    return max(found, key=lambda e: int(e.Severity), default=None)


@dataclass(frozen=True)
class MonitorStatus:
    Capacity: CapacityState
    HasUnavailableData: bool
    UsagePaused: bool
    AccountId: Optional[str]
    WindowId: Optional[str]
    Notice: Optional[str]

    @staticmethod
    def evaluate(accounts: Sequence[ProviderAccount], snapshots: Mapping[str, QuotaSnapshot],
                 settings: RadarSettings) -> "MonitorStatus":
        enabled = [a for a in accounts if a.Enabled]
        if not enabled:
            return MonitorStatus(CapacityState.Unknown, False, False, None, None,
                                 "No connected accounts" if not accounts else "Monitoring paused")
        unavailable = paused = False
        windows: list[tuple[str, QuotaWindow, CapacityState]] = []
        for account in enabled:
            s = snapshots.get(account.Id)
            if s is None or not s.belongs_to(account) or s.Health not in (SourceHealth.Healthy, SourceHealth.Demo):
                unavailable = True
                continue
            paused |= s.UsageAllowed is False
            if not s.Windows:
                unavailable = True
            for w in s.Windows:
                rule = settings.rule_for(account.Id, w.Id)
                state = w.capacity(rule.LowThreshold, rule.CriticalThreshold, rule.InfoThreshold)
                unavailable |= state == CapacityState.Unknown
                windows.append((account.Id, w, state))
        rank = {CapacityState.Exhausted: 5, CapacityState.Critical: 4, CapacityState.Low: 3,
                CapacityState.Info: 2, CapacityState.Healthy: 1}
        notice = "Provider reports usage is paused" if paused else "Some current usage data is unavailable" if unavailable else None
        if not windows:
            return MonitorStatus(CapacityState.Unknown, unavailable, paused, None, None, notice)
        acc, window, state = sorted(windows, key=lambda w: (-rank.get(w[2], 0),
                                                            w[1].remaining_percent if w[1].remaining_percent is not None else 1e18))[0]
        return MonitorStatus(state, unavailable, paused, acc, window.Id, notice)


@dataclass(frozen=True)
class ResetSummary:
    ReportedCount: Optional[int]
    EarliestExpiry: Optional[datetime]
    IsLastReported: bool
    HasExpiredDetails: bool

    @staticmethod
    def evaluate(accounts: Iterable[ProviderAccount], snapshots: Mapping[str, QuotaSnapshot], now: datetime) -> "ResetSummary":
        total = 0
        has_count = partial = expired = False
        earliest: Optional[datetime] = None
        for account in accounts:
            if not account.Enabled or account.Provider != "OpenAI":
                continue
            s = snapshots.get(account.Id)
            if s is None or not s.belongs_to(account) or s.Reserve is None:
                partial = True
                continue
            reserve = s.Reserve
            if reserve.AvailableCount is not None:
                total += reserve.AvailableCount
                has_count = True
            else:
                partial = True
            partial |= s.Health not in (SourceHealth.Healthy, SourceHealth.Demo)
            nxt = reserve.earliest_expiry(now)
            if nxt is not None and (earliest is None or nxt < earliest):
                earliest = nxt
            expired |= reserve.AvailableCount != 0 and any(
                c.Status == "available" and c.ExpiresAt is not None and c.ExpiresAt <= now for c in reserve.Credits or ())
        return ResetSummary(total if has_count else None, earliest, partial or expired, expired)


def next_refresh_after_success(snapshot: QuotaSnapshot, account: ProviderAccount, now: datetime) -> datetime:
    """Next normal poll, or an earlier known reset/expiry boundary."""
    nxt = now + timedelta(minutes=account.effective_polling_minutes)
    boundaries = [w.ResetsAt for w in snapshot.Windows]
    if snapshot.Reserve and snapshot.Reserve.Credits:
        boundaries += [c.ExpiresAt for c in snapshot.Reserve.Credits if c.Status == "available"]
    for b in boundaries:
        if b is not None and now < b < nxt:
            nxt = b
    return nxt


def failure_backoff(count: int) -> timedelta:
    return timedelta(seconds=min(1800, 60 * 2 ** min(5, count)))


def retention_cutoff(now: datetime, days: int) -> datetime:
    return now - timedelta(days=max(1, min(365, days)))


def prune_alerts(alerts: Iterable[AlertEvent], now: datetime, days: int) -> list[AlertEvent]:
    cutoff = retention_cutoff(now, days)
    return [a for a in alerts if a.CreatedAt >= cutoff]


__all__ = ["AlertEvent", "AlertEvaluator", "icon_alert", "MonitorStatus", "ResetSummary", "next_refresh_after_success",
           "failure_backoff", "retention_cutoff", "prune_alerts", "fmt_whole"]
