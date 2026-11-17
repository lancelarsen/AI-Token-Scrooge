"""User preferences, alert rules, reminders and ordering (port of RadarSettings.cs and friends)."""
from __future__ import annotations

import enum
import math
from dataclasses import dataclass, field
from datetime import datetime
from typing import Iterable, Optional, Sequence

from .model import replace

HISTORY_RETENTION_DAYS = 90


class AccountSortMode(enum.Enum):
    NextReset = 0
    CustomOrder = 1
    MostRemaining = 2
    LeastRemaining = 3


class ReminderSeverity(enum.IntEnum):
    Info = 0
    Warning = 1
    Critical = 2


@dataclass(frozen=True)
class TimedReminder:
    Id: str
    Amount: int
    Unit: str  # "Days" or "Hours"
    Severity: ReminderSeverity

    @property
    def hours(self) -> float:
        return self.Amount * 24.0 if self.Unit == "Days" else float(self.Amount)

    @property
    def is_valid(self) -> bool:
        return self.Amount > 0 and self.Unit in ("Days", "Hours") and isinstance(self.Severity, ReminderSeverity)

    @staticmethod
    def defaults(expiration: bool) -> tuple["TimedReminder", ...]:
        hours = (240, 120, 72, 48, 24, 12, 6, 3, 1) if expiration else (120, 72, 48, 24, 12, 6, 3, 1)
        return tuple(TimedReminder(f"default-{h}", h // 24 if h >= 24 else h, "Days" if h >= 24 else "Hours",
                                   ReminderSeverity.Info if h >= 120 else ReminderSeverity.Warning if h >= 72
                                   else ReminderSeverity.Critical) for h in hours)

    @staticmethod
    def due(reminders: Sequence["TimedReminder"], at: datetime, now: datetime) -> Optional["TimedReminder"]:
        """Catch up with the nearest due reminder rather than flooding every missed milestone."""
        if at <= now:
            return None
        left = (at - now).total_seconds() / 3600
        candidates = [r for r in reminders if r.is_valid and left <= r.hours]
        if not candidates:
            return None
        return sorted(candidates, key=lambda r: (r.hours, -int(r.Severity)))[0]


@dataclass(frozen=True)
class AlertThresholds:
    Warning: float
    Critical: float
    Info: float = 100

    @property
    def is_valid(self) -> bool:
        return 0 <= self.Critical < self.Warning <= self.Info <= 100

    @staticmethod
    def remaining_at(position: float, width: float) -> float:
        if width <= 0 or not math.isfinite(position) or not math.isfinite(width):
            return 0
        return float(math.floor(max(0.0, min(1.0, 1 - position / width)) * 100 + 0.5))

    def move_info(self, remaining: float) -> "AlertThresholds":
        return replace(self, Info=max(self.Warning, min(100, round(remaining))))

    def move(self, warning: bool, remaining: float) -> "AlertThresholds":
        value = math.floor(remaining + 0.5)
        if warning:
            return replace(self, Warning=max(math.floor(self.Critical) + 1, min(self.Info, value)))
        return replace(self, Critical=max(0, min(math.ceil(self.Warning) - 1, value)))


@dataclass(frozen=True)
class QuotaAlertSection:
    AccountId: str
    WindowId: str


@dataclass(frozen=True)
class AlertRule:
    AccountId: str
    WindowId: Optional[str] = None
    LowThreshold: Optional[float] = None
    CriticalThreshold: Optional[float] = None
    Low: bool = True
    Critical: bool = True
    Exhausted: bool = True
    Restored: bool = True
    Expiry: bool = True
    InfoThreshold: Optional[float] = None
    Info: bool = True


@dataclass(frozen=True)
class RadarSettings:
    Notifications: bool = True
    Sound: bool = True
    QuietHours: bool = True
    QuietStartHour: int = 0
    QuietEndHour: int = 8
    LowThreshold: float = 30
    CriticalThreshold: float = 10
    LaunchAtSignIn: bool = True
    CloseToTray: bool = True
    Rules: Optional[tuple[AlertRule, ...]] = None
    TimeZoneId: str = ""  # Empty means the computer's local time zone.
    AccountSort: AccountSortMode = AccountSortMode.CustomOrder
    AccountOrder: Optional[tuple[str, ...]] = None
    DisabledCapacityAlerts: Optional[tuple[QuotaAlertSection, ...]] = None
    SoundTheme: str = "Ducks"
    SoundVolume: float = 65
    ResetExpiryReminders: Optional[tuple[TimedReminder, ...]] = None
    QuotaRefreshReminders: Optional[tuple[TimedReminder, ...]] = None
    InfoThreshold: float = 50
    AlertOnIcon: bool = True
    CustomLineColors: Optional[tuple[str, ...]] = None
    HistoryRetentionDays: int = HISTORY_RETENTION_DAYS
    HistoryEnabled: bool = True
    HiddenTimelineAccounts: Optional[tuple[str, ...]] = None

    @property
    def effective_reset_reminders(self) -> tuple[TimedReminder, ...]:
        return self.ResetExpiryReminders if self.ResetExpiryReminders is not None else TimedReminder.defaults(True)

    @property
    def effective_refresh_reminders(self) -> tuple[TimedReminder, ...]:
        return self.QuotaRefreshReminders if self.QuotaRefreshReminders is not None else TimedReminder.defaults(False)

    @property
    def retention_days(self) -> int:
        return max(1, min(365, int(self.HistoryRetentionDays)))

    def capacity_alerts_enabled(self, account_id: str, window_id: str) -> bool:
        return not any(s.AccountId == account_id and s.WindowId == window_id for s in self.DisabledCapacityAlerts or ())

    def with_capacity_alerts(self, account_id: str, window_id: str, enabled: bool) -> "RadarSettings":
        disabled = [s for s in self.DisabledCapacityAlerts or () if s.AccountId != account_id or s.WindowId != window_id]
        if not enabled:
            disabled.append(QuotaAlertSection(account_id, window_id))
        return replace(self, DisabledCapacityAlerts=tuple(disabled))

    def with_window_thresholds(self, account_id: str, window_id: str, low: float, critical: float,
                               info: Optional[float] = None) -> "RadarSettings":
        info_value = info if info is not None else max(self.InfoThreshold, low)
        if not AlertThresholds(low, critical, info_value).is_valid:
            raise ValueError("Use 0 ≤ critical < warning ≤ 100.")
        rules = self.Rules or ()
        stored = next((r for r in rules if r.AccountId == account_id and r.WindowId == window_id), None) \
            or next((r for r in rules if r.AccountId == account_id and r.WindowId is None), None) \
            or AlertRule(account_id)
        updated = replace(stored, WindowId=window_id, LowThreshold=low, CriticalThreshold=critical, InfoThreshold=info_value)
        kept = tuple(r for r in rules if r.AccountId != account_id or r.WindowId != window_id)
        return replace(self, Rules=kept + (updated,))

    def with_account_rule(self, account_id: str, **flags) -> "RadarSettings":
        rules = self.Rules or ()
        stored = next((r for r in rules if r.AccountId == account_id and r.WindowId is None), None) or AlertRule(account_id)
        # Account-level toggles apply to every window rule of that account.
        updated = [replace(r, **flags) if r.AccountId == account_id else r for r in rules
                   if not (r.AccountId == account_id and r.WindowId is None)]
        return replace(self, Rules=tuple(updated) + (replace(stored, **flags),))

    def rule_for(self, account_id: str, window_id: Optional[str] = None) -> AlertRule:
        rules = self.Rules or ()
        account = next((r for r in rules if r.AccountId == account_id and r.WindowId is None), None) or AlertRule(account_id)
        window = account if window_id is None else next(
            (r for r in rules if r.AccountId == account_id and r.WindowId == window_id), None) or account
        low = _first(window.LowThreshold, account.LowThreshold, self.LowThreshold)
        return replace(window,
                       LowThreshold=low,
                       CriticalThreshold=_first(window.CriticalThreshold, account.CriticalThreshold, self.CriticalThreshold),
                       InfoThreshold=_first(window.InfoThreshold, account.InfoThreshold,
                                            max(self.InfoThreshold, _first(window.LowThreshold, account.LowThreshold, self.LowThreshold))))

    @property
    def has_valid_thresholds(self) -> bool:
        def valid(low, critical, info):
            return 0 <= critical < low <= 100 and low <= info <= 100
        if not valid(self.LowThreshold, self.CriticalThreshold, self.InfoThreshold):
            return False
        for r in self.Rules or ():
            eff = self.rule_for(r.AccountId, r.WindowId)
            if not valid(eff.LowThreshold, eff.CriticalThreshold, eff.InfoThreshold):
                return False
        return True

    def timeline_includes(self, account_id: str) -> bool:
        return account_id not in (self.HiddenTimelineAccounts or ())

    def with_timeline_account(self, account_id: str, visible: bool) -> "RadarSettings":
        hidden = [i for i in self.HiddenTimelineAccounts or () if i != account_id]
        if not visible:
            hidden.append(account_id)
        return replace(self, HiddenTimelineAccounts=tuple(dict.fromkeys(hidden)))


def _first(*values):
    for v in values:
        if v is not None:
            return v
    return None


def normalize_order(saved: Optional[Iterable[str]], connected: Iterable[str]) -> list[str]:
    ids = list(dict.fromkeys(connected))
    live = set(ids)
    return list(dict.fromkeys([i for i in saved or () if i in live] + ids))


def move_in_order(order: Sequence[str], account_id: str, target_index: int) -> list[str]:
    result = list(order)
    if account_id not in result:
        return result
    result.remove(account_id)
    result.insert(max(0, min(len(result), target_index)), account_id)
    return result
