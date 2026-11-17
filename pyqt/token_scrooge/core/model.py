"""Shared account and quota contract (port of AIResetRadar.Core/Quota.cs).

Keep unknown values as None. Clamp visual fills, not raw accounting data: a
negative absolute remaining balance can represent overage.
"""
from __future__ import annotations

import dataclasses
import enum
from dataclasses import dataclass, field
from datetime import datetime, timedelta
from decimal import ROUND_HALF_UP, Decimal
from typing import Optional


class UsageUnit(enum.Enum):
    Percent = 0
    Credits = 1
    Requests = 2
    Acus = 3


class SourceHealth(enum.Enum):
    Healthy = 0
    Loading = 1
    Stale = 2
    Offline = 3
    AuthenticationRequired = 4
    PermissionRequired = 5
    Unsupported = 6
    Demo = 7


class AllowanceProvenance(enum.Enum):
    Unknown = 0
    Provider = 1
    Manual = 2


class CapacityState(enum.Enum):
    Unknown = 0
    Healthy = 1
    Low = 2
    Critical = 3
    Exhausted = 4
    Info = 5


class CredentialProfileLocation(enum.Enum):
    LegacyDataDirectory = 0
    LocalSecurityDirectory = 1


PROVIDERS = ("OpenAI", "GitHub Copilot", "Claude", "Devin")


def replace(obj, **changes):
    return dataclasses.replace(obj, **changes)


def round_away(value: float, digits: int = 0) -> float:
    """.NET formats decimals with midpoint-away-from-zero rounding; Python rounds to even."""
    quant = Decimal(1).scaleb(-digits)
    return float(Decimal(str(value)).quantize(quant, rounding=ROUND_HALF_UP))


def fmt_whole(value: float) -> str:
    return f"{round_away(value):.0f}"


def fmt_count(value: float) -> str:
    return f"{round_away(value):,.0f}"


@dataclass(frozen=True, eq=True)
class ProviderAccount:
    Id: str
    Provider: str
    Nickname: str
    Subject: str
    Scope: str
    Enabled: bool = True
    Allowance: Optional[float] = None
    Unit: UsageUnit = UsageUnit.Credits
    Login: Optional[str] = None
    CredentialId: Optional[str] = None
    PollingMinutes: int = 2
    LineColor: Optional[str] = None
    ProfileLocation: CredentialProfileLocation = CredentialProfileLocation.LegacyDataDirectory

    @property
    def effective_polling_minutes(self) -> int:
        return max(1, min(30, int(self.PollingMinutes)))

    @property
    def credential(self) -> str:
        return self.CredentialId or self.Id

    def scope_equals(self, other: Optional[str]) -> bool:
        if other is None:
            return False
        if self.Provider == "GitHub Copilot":
            return self.Scope.lower() == other.lower()
        return self.Scope == other


@dataclass(frozen=True)
class QuotaWindow:
    Id: str
    Name: str
    Unit: UsageUnit
    Used: Optional[float]
    Limit: Optional[float]
    ResetsAt: Optional[datetime]
    DurationMinutes: Optional[int] = None
    AllowanceSource: AllowanceProvenance = AllowanceProvenance.Unknown

    @property
    def used_percent(self) -> Optional[float]:
        if self.Used is None or self.Used < 0:
            return None
        if self.Unit == UsageUnit.Percent:
            return self.Used
        return self.Used / self.Limit * 100 if self.Limit is not None and self.Limit > 0 else None

    @property
    def remaining(self) -> Optional[float]:
        if self.Used is None or self.Used < 0:
            return None
        if self.Unit == UsageUnit.Percent:
            return max(0.0, 100 - self.Used)
        return self.Limit - self.Used if self.Limit is not None and self.Limit >= 0 else None

    @property
    def remaining_percent(self) -> Optional[float]:
        used = self.used_percent
        return None if used is None else max(0.0, min(100.0, 100 - used))

    @property
    def fill(self) -> float:
        used = self.used_percent
        return 0.0 if used is None else max(0.0, min(1.0, used / 100))

    def capacity(self, low: float = 30, critical: float = 10, info: float = 50) -> CapacityState:
        left = self.remaining_percent
        if left is None:
            return CapacityState.Unknown
        if left <= 0:
            return CapacityState.Exhausted
        if left <= critical:
            return CapacityState.Critical
        if left <= low:
            return CapacityState.Low
        if left <= info:
            return CapacityState.Info
        return CapacityState.Healthy


@dataclass(frozen=True)
class ResetCredit:
    Id: str
    Status: str
    ExpiresAt: Optional[datetime]
    Title: Optional[str] = None


@dataclass(frozen=True)
class ResetReserve:
    AvailableCount: Optional[int]
    Credits: Optional[tuple[ResetCredit, ...]]

    @property
    def has_partial_details(self) -> bool:
        if not (self.AvailableCount and self.AvailableCount > 0):
            return False
        available = sum(1 for c in (self.Credits or ()) if c.Status == "available")
        return self.Credits is None or available < self.AvailableCount

    def earliest_expiry(self, now: datetime) -> Optional[datetime]:
        if self.AvailableCount == 0 or self.Credits is None:
            return None
        times = [c.ExpiresAt for c in self.Credits if c.Status == "available" and c.ExpiresAt and c.ExpiresAt > now]
        return min(times) if times else None


@dataclass(frozen=True)
class QuotaSnapshot:
    AccountId: str
    Subject: str
    Scope: str
    ObservedAt: datetime
    Windows: tuple[QuotaWindow, ...]
    Reserve: Optional[ResetReserve]
    Health: SourceHealth = SourceHealth.Healthy
    Message: Optional[str] = None
    ProviderDataAt: Optional[datetime] = None
    UsageAllowed: Optional[bool] = None
    PlanType: Optional[str] = None
    # History run metadata; never persisted with current state.
    HistoryContinuousFrom: Optional[datetime] = field(default=None, compare=False, metadata={"transient": True})
    HistoryMaximumGapSeconds: float = field(default=0, compare=False, metadata={"transient": True})

    def belongs_to(self, account: ProviderAccount) -> bool:
        return (self.AccountId == account.Id and self.Subject == account.Subject
                and (account.Scope == "unresolved" or account.scope_equals(self.Scope)))


class ResetTime:
    @staticmethod
    def countdown(reset: Optional[datetime], now: datetime) -> str:
        if reset is None:
            return "Reset time unavailable"
        duration = reset - now
        if duration <= timedelta(0):
            return "Reset due — checking"
        seconds = duration.total_seconds()
        if seconds < 60:
            return "In less than a minute"
        days = int(seconds // 86400)
        hours = int((seconds % 86400) // 3600)
        minutes = int((seconds % 3600) // 60)
        if days >= 1:
            return f"In {days}d {hours}h"
        if seconds >= 3600:
            return f"In {int(seconds // 3600)}h {minutes}m"
        return f"In {minutes}m"

    @staticmethod
    def summary(reset: Optional[datetime], now: datetime) -> str:
        countdown = ResetTime.countdown(reset, now)
        return f"Resets: {countdown[3:]}" if countdown.startswith("In ") else countdown


class ReconnectError(ValueError):
    pass


def accept_reconnect(original: ProviderAccount, replacement: ProviderAccount) -> ProviderAccount:
    if (original.Id != replacement.Id or original.Provider != replacement.Provider
            or original.Subject != replacement.Subject
            or (original.Scope != "unresolved" and not original.scope_equals(replacement.Scope))):
        raise ReconnectError("This is a different account or workspace. Reconnect the original account, "
                             "or add a separate connection.")
    return replace(replacement, Enabled=original.Enabled, PollingMinutes=original.effective_polling_minutes,
                   LineColor=original.LineColor)


class AccountRefreshLease:
    """Immutable account objects act as generations: replacing even an equal record invalidates reads."""

    def __init__(self, account: ProviderAccount):
        self._account = account

    def is_current(self, current: Optional[ProviderAccount]) -> bool:
        return current is self._account and current.Enabled
