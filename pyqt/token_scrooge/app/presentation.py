"""Qt-free presentation logic: dashboard rows, horizon events, history projection and
calendar-day geometry. Ported from App/Presentation so it stays unit-testable."""
from __future__ import annotations

import math
from dataclasses import dataclass, field
from datetime import date, datetime, time, timedelta, timezone, tzinfo
from typing import Callable, Iterable, Optional, Sequence

from ..core.alerts import AlertEvaluator  # noqa: F401  (re-exported for callers)
from ..core.labels import capitalize_words, plan_label
from ..core.model import (AllowanceProvenance, CapacityState, ProviderAccount, QuotaSnapshot, QuotaWindow,
                          ResetCredit, ResetReserve, ResetTime, SourceHealth, UsageUnit, fmt_count, fmt_whole, replace)
from ..core.settings import AccountSortMode, RadarSettings, normalize_order

# ------------------------------------------------------------------ dashboard rows


@dataclass(frozen=True)
class QuotaRow:
    Account: ProviderAccount
    Snapshot: QuotaSnapshot
    Window: QuotaWindow
    Low: float = 30
    Critical: float = 10
    Info: float = 50

    @property
    def account_label(self) -> str:
        return f"{self.Account.Nickname} · {plan_label(self.Account.Provider, self.Snapshot.PlanType)}"

    @property
    def window_title(self) -> str:
        return capitalize_words(self.Window.Name)

    @property
    def capacity(self) -> CapacityState:
        return self.Window.capacity(self.Low, self.Critical, self.Info)

    @property
    def _display_percent(self) -> bool:
        return self.Window.Unit == UsageUnit.Percent or self.Account.Provider in ("GitHub Copilot", "Devin")

    @property
    def _github_without_percentage(self) -> bool:
        return self.Account.Provider == "GitHub Copilot" and self.Window.used_percent is None

    @property
    def used_text(self) -> str:
        w = self.Window
        if self._github_without_percentage:
            unit = "Requests" if w.Unit == UsageUnit.Requests else "Credits"
            return f"{fmt_count(w.Used)} {unit} Used" if w.Used is not None else "Usage Unavailable"
        if self._display_percent:
            return f"{fmt_whole(w.used_percent)}% used" if w.used_percent is not None else "Usage unavailable"
        return f"{fmt_count(w.Used)} used" if w.Used is not None else "Usage unavailable"

    @property
    def left_text(self) -> str:
        w = self.Window
        if self._github_without_percentage:
            return f"{fmt_count(w.Limit)} Limit" if w.Limit is not None else "Limit Unavailable"
        if self._display_percent:
            return f"{fmt_whole(w.remaining_percent)}% left" if w.remaining_percent is not None else "Left unavailable"
        left = w.remaining
        if left is None:
            return "Limit unavailable"
        return f"{fmt_count(-left)} over" if left < 0 else f"{fmt_count(left)} left"

    @property
    def card_note(self) -> str:
        s = self.Snapshot
        return "" if s.Health == SourceHealth.Healthy and s.UsageAllowed is not False else self.source_note

    @property
    def source_note(self) -> str:
        s = self.Snapshot
        if s.Health == SourceHealth.Demo:
            return "Demo data"
        if s.Health != SourceHealth.Healthy:
            return f"{health_label(s.Health)}: {s.Message or ''}".rstrip(": ")
        if s.UsageAllowed is False:
            return "Provider reports usage is paused"
        if s.Message:
            return s.Message
        return "User-entered allowance · Billing data may lag" if self.Window.AllowanceSource == AllowanceProvenance.Manual else ""


def health_label(health: SourceHealth) -> str:
    return {SourceHealth.AuthenticationRequired: "Sign-In Required", SourceHealth.PermissionRequired: "Permission Required"}.get(
        health, health.name)


@dataclass(frozen=True)
class HorizonEvent:
    AccountId: str
    Title: str
    Detail: str
    At: datetime
    IsExpiry: bool
    WindowId: Optional[str] = None
    Provider: Optional[str] = None
    PlanBadge: Optional[str] = None


def history_eligible(provider: str, window: QuotaWindow) -> bool:
    if provider == "OpenAI":
        return window.DurationMinutes == 10080
    if provider == "Claude":
        return window.DurationMinutes == 10080 or (window.Id == "weekly" and window.DurationMinutes is None)
    if provider == "Devin":
        return window.Id == "devin/monthly"
    if provider == "GitHub Copilot":
        return window.Id == "copilot/monthly"
    return False


def countdown_target(row: QuotaRow, now: datetime) -> tuple[Optional[datetime], bool]:
    """(time, is_expiry): an earlier earned-reset expiry takes the weekly row's countdown slot."""
    refresh = row.Window.ResetsAt
    reserve = row.Snapshot.Reserve
    if history_eligible(row.Account.Provider, row.Window) and reserve is not None:
        expiry = reserve.earliest_expiry(now)
        if expiry is not None and (refresh is None or expiry < refresh):
            return expiry, True
    return refresh, False


def countdown_label(reset: Optional[datetime], now: datetime) -> str:
    summary = ResetTime.summary(reset, now)
    return summary[8:] if summary.startswith("Resets: ") else summary


class DashboardState:
    def __init__(self):
        self.accounts: list[ProviderAccount] = []
        self.snapshots: dict[str, QuotaSnapshot] = {}
        self.is_demo = False
        self.settings = RadarSettings()

    def account(self, account_id: str) -> Optional[ProviderAccount]:
        return next((a for a in self.accounts if a.Id == account_id), None)

    def ordered_accounts(self) -> list[ProviderAccount]:
        order = normalize_order(self.settings.AccountOrder, [a.Id for a in self.accounts])
        ranks = {i: r for r, i in enumerate(order)}

        def remaining(a: ProviderAccount):
            s = self.snapshots.get(a.Id)
            if not a.Enabled or s is None:
                return None
            values = [w.remaining_percent for w in s.Windows if w.remaining_percent is not None]
            # Mirrors LINQ Min over nullable decimals: nulls are ignored.
            return min(values) if values else None

        def nxt(a: ProviderAccount):
            s = self.snapshots.get(a.Id)
            values = [w.ResetsAt for w in s.Windows if w.ResetsAt] if a.Enabled and s else []
            return min(values) if values else datetime.max.replace(tzinfo=timezone.utc)

        mode = self.settings.AccountSort
        if mode == AccountSortMode.CustomOrder:
            return sorted(self.accounts, key=lambda a: ranks[a.Id])
        if mode == AccountSortMode.MostRemaining:
            return sorted(self.accounts, key=lambda a: (remaining(a) is None, -(remaining(a) or 0)))
        if mode == AccountSortMode.LeastRemaining:
            return sorted(self.accounts, key=lambda a: (remaining(a) is None, remaining(a) or 0))
        return sorted(self.accounts, key=nxt)

    def rows_for(self, account: ProviderAccount) -> list[QuotaRow]:
        s = self.snapshots.get(account.Id)
        if not account.Enabled or s is None:
            return []
        rows = []
        for w in s.Windows:
            rule = self.settings.rule_for(account.Id, w.Id)
            rows.append(QuotaRow(account, s, w, rule.LowThreshold, rule.CriticalThreshold, rule.InfoThreshold))
        return rows

    def rows(self) -> list[QuotaRow]:
        return [r for a in self.ordered_accounts() for r in self.rows_for(a)]

    def timeline_badge(self, account: ProviderAccount) -> Optional[str]:
        same = sum(1 for a in self.accounts if a.Enabled and a.Provider == account.Provider)
        if same <= 1:
            return None
        s = self.snapshots.get(account.Id)
        return plan_label(account.Provider, s.PlanType if s else None)

    def events(self, now: datetime) -> list[HorizonEvent]:
        result = [HorizonEvent(r.Account.Id, f"{r.Account.Provider} / {r.account_label}", r.window_title, r.Window.ResetsAt,
                               False, r.Window.Id, r.Account.Provider, self.timeline_badge(r.Account))
                  for r in self.rows() if r.Window.ResetsAt and r.Window.ResetsAt > now]
        for a in self.accounts:
            s = self.snapshots.get(a.Id)
            if not a.Enabled or s is None or s.Reserve is None or s.Reserve.AvailableCount == 0:
                continue
            for c in s.Reserve.Credits or ():
                if c.Status == "available" and c.ExpiresAt and c.ExpiresAt > now:
                    result.append(HorizonEvent(a.Id, "Earned Reset Expires",
                                               f"{a.Provider} / {a.Nickname} · {plan_label(a.Provider, s.PlanType)}",
                                               c.ExpiresAt, True, Provider=a.Provider, PlanBadge=self.timeline_badge(a)))
        return sorted(result, key=lambda e: e.At)

    # -------------------------------------------------------------- demo data
    def load_demo(self, now: datetime) -> None:
        self.is_demo = True
        self.accounts.clear()
        self.snapshots.clear()
        personal = ProviderAccount("demo-personal", "OpenAI", "Personal", "demo@example.invalid", "demo")
        work = ProviderAccount("demo-work", "OpenAI", "Work", "work@example.invalid", "demo-work")
        github = ProviderAccount("demo-github", "GitHub Copilot", "Work", "demo-github", "personal", Allowance=10000)
        claude = ProviderAccount("demo-claude", "Claude", "Personal", "claude@example.invalid", "demo-claude")
        self.accounts += [personal, github, work, claude]
        P, D = UsageUnit.Percent, SourceHealth.Demo
        self.snapshots[personal.Id] = QuotaSnapshot(personal.Id, personal.Subject, personal.Scope, now, (
            QuotaWindow("codex/primary", "Codex · 5-hour", P, 38, 100, now + timedelta(hours=2, minutes=14), 300),
            QuotaWindow("codex/secondary", "Codex · Weekly", P, 74, 100, now + timedelta(days=2), 10080)),
            ResetReserve(2, (ResetCredit("demo-r1", "available", now + timedelta(days=4)),
                             ResetCredit("demo-r2", "available", now + timedelta(days=9)))), D, PlanType="plus")
        self.snapshots[work.Id] = QuotaSnapshot(work.Id, work.Subject, work.Scope, now, (
            QuotaWindow("codex/primary", "Codex · 5-hour", P, 19, 100, now + timedelta(hours=3, minutes=44), 300),
            QuotaWindow("codex/secondary", "Codex · Weekly", P, 58, 100, now + timedelta(days=5), 10080)), None, D, PlanType="pro")
        self.snapshots[claude.Id] = QuotaSnapshot(claude.Id, claude.Subject, claude.Scope, now, (
            QuotaWindow("session", "Current Session", P, 12, 100, now + timedelta(hours=3), 300),
            QuotaWindow("weekly", "Weekly Limit", P, 94, 100, now + timedelta(days=4), 10080)), None, D, PlanType="pro")
        utc = now.astimezone(timezone.utc)
        month_end = (datetime(utc.year + (utc.month == 12), utc.month % 12 + 1, 1, tzinfo=timezone.utc))
        self.snapshots[github.Id] = QuotaSnapshot(github.Id, github.Subject, github.Scope, now, (
            QuotaWindow("copilot/monthly", "AI credits · Monthly (10,000)", UsageUnit.Credits, 8200, 10000, month_end),),
            None, D, PlanType="individual_max")

    def clear_demo(self) -> None:
        self.is_demo = False
        self.accounts.clear()
        self.snapshots.clear()


def demo_history(state: DashboardState, now: datetime) -> dict[str, list[QuotaSnapshot]]:
    """Synthetic step-like history for demo mode (port of HistoryDemo)."""
    import random
    result: dict[str, list[QuotaSnapshot]] = {}
    for account in state.accounts:
        current = state.snapshots.get(account.Id)
        if current is None:
            continue
        window = next((w for w in current.Windows if history_eligible(account.Provider, w)), None)
        if window is None:
            continue
        rng = random.Random(sum(map(ord, account.Id)))
        samples: list[QuotaSnapshot] = []
        start = now - timedelta(days=7)
        used = rng.uniform(5, 25)
        reset_at = start + timedelta(days=rng.uniform(1.5, 3.5))
        t = start
        while t < now:
            if t >= reset_at:
                used = rng.uniform(0, 6)
                reset_at = reset_at + timedelta(days=7)
            if rng.random() < 0.18:
                used = min(99, used + rng.uniform(1, 9))
            limit = window.Limit if window.Unit != UsageUnit.Percent else 100
            value = used if window.Unit == UsageUnit.Percent else used / 100 * (limit or 100)
            samples.append(replace(current, ObservedAt=t, Windows=(replace(window, Used=value, ResetsAt=reset_at),)))
            t += timedelta(minutes=rng.choice((20, 30, 45, 60)))
        target = window.used_percent or 0
        if samples and window.Unit == UsageUnit.Percent:
            samples.append(replace(current, ObservedAt=now - timedelta(minutes=5),
                                   Windows=(replace(window, Used=target),)))
        result[account.Id] = samples
    return result


# ------------------------------------------------------------------ usage history

@dataclass(frozen=True)
class HistoryPoint:
    At: datetime
    RemainingPercent: float
    WindowId: str
    StartsSegment: bool
    PreviousObservedAt: Optional[datetime] = None
    ReplenishedFrom: Optional[float] = None
    InterpolatedFromPrevious: bool = False


@dataclass(frozen=True)
class HistoryEvent:
    AccountId: str
    WindowId: Optional[str]
    Id: str
    At: datetime
    IsExpiry: bool
    ReportedAt: datetime
    Detail: str


@dataclass(frozen=True)
class HistorySeries:
    Account: ProviderAccount
    Period: str
    PlanLabel: str
    Points: tuple[HistoryPoint, ...]
    Events: tuple[HistoryEvent, ...]
    Status: Optional[str] = None


def select_history_window(provider: str, windows: Sequence[QuotaWindow]) -> tuple[Optional[QuotaWindow], bool]:
    eligible = [w for w in windows if history_eligible(provider, w)]
    if len(eligible) < 2:
        return (eligible[0] if eligible else None), False
    canonical = [w for w in eligible if (provider == "OpenAI" and w.Id.startswith("codex/"))
                 or (provider == "Claude" and w.Id == "weekly")]
    return (canonical[0], False) if len(canonical) == 1 else (None, True)


def build_history(account: ProviderAccount, snapshots: Iterable[QuotaSnapshot], start: datetime, through: datetime,
                  polling: Optional[timedelta] = None) -> HistorySeries:
    """Projects recorded observations without manufacturing usage or reset events."""
    period = "Monthly" if account.Provider in ("Devin", "GitHub Copilot") else "Weekly"
    by_time: dict[datetime, QuotaSnapshot] = {}
    for s in sorted((s for s in snapshots if s.AccountId == account.Id and s.ObservedAt <= through), key=lambda s: s.ObservedAt):
        by_time[s.ObservedAt] = s
    ordered = list(by_time.values())
    prior = next((s for s in reversed(ordered) if s.ObservedAt < start), None)
    interval = polling or timedelta(minutes=account.effective_polling_minutes)
    max_gap = max(timedelta(minutes=15), interval * 3)
    relevant = [s for s in ordered if s.ObservedAt >= start or s is prior]
    anchors = {s.HistoryContinuousFrom for s in relevant if s.HistoryContinuousFrom}
    current = next((s for s in reversed(ordered) if s.belongs_to(account)), None)
    plan = plan_label(account.Provider, current.PlanType if current else None)
    points: list[HistoryPoint] = []
    events: list[HistoryEvent] = []
    pending: dict[str, HistoryEvent] = {}
    expirations: dict[str, HistoryEvent] = {}
    prev_window = prev_snapshot = prev_point = None
    missed = False
    status: Optional[str] = None
    for snap in relevant:
        if not snap.belongs_to(account):
            prev_point = prev_window = prev_snapshot = None
            status = "Waiting for Fresh Usage"
            continue
        if snap.Health not in (SourceHealth.Healthy, SourceHealth.Demo):
            missed = True
            status = "Waiting for Fresh Usage"
            continue
        window, ambiguous = select_history_window(account.Provider, snap.Windows)
        status = f"Multiple {period} Limits Reported" if ambiguous else f"No {period} Limit Reported" if window is None else None
        if window is not None and window.ResetsAt is not None:
            at = window.ResetsAt
            old = pending.get(window.Id)
            if old is not None and old.At != at and snap.ObservedAt >= old.At:
                events.append(old)
            pending[window.Id] = HistoryEvent(account.Id, window.Id, f"refresh:{window.Id}:{int(at.timestamp() * 1000)}", at,
                                              False, snap.ObservedAt, f"Scheduled {period} Quota Refresh")
        for credit in (snap.Reserve.Credits if snap.Reserve and snap.Reserve.Credits else ()):
            if credit.Status == "available" and credit.ExpiresAt:
                expirations[credit.Id] = HistoryEvent(account.Id, None, f"credit:{credit.Id}", credit.ExpiresAt, True,
                                                      snap.ObservedAt, "Last Reported Earned Reset Expiration")
            elif credit.Status in ("used", "redeemed") and credit.Id in expirations and snap.ObservedAt < expirations[credit.Id].At:
                del expirations[credit.Id]
        remaining = window.remaining_percent if window else None
        if remaining is None:
            missed = True
            status = status or "Remaining Capacity Unavailable"
            continue
        if snap.ObservedAt < start and start - snap.ObservedAt > max_gap and snap.ObservedAt not in anchors:
            continue
        starts = (prev_point is None or prev_window is None or prev_snapshot is None or window.Id != prev_window.Id
                  or window.Unit != prev_window.Unit or window.Limit != prev_window.Limit
                  or window.AllowanceSource != prev_window.AllowanceSource or snap.PlanType != prev_snapshot.PlanType
                  or not _same_scope(account.Provider, snap.Scope, prev_snapshot.Scope))
        estimated = (not starts and (missed or (snap.ObservedAt - prev_point.At > max_gap and (
            snap.HistoryContinuousFrom != prev_point.At or snap.HistoryMaximumGapSeconds > max_gap.total_seconds()))))
        replenished = not starts and not estimated and remaining > prev_point.RemainingPercent
        point = HistoryPoint(snap.ObservedAt, remaining, window.Id, starts, None if starts else prev_point.At,
                             prev_point.RemainingPercent if replenished else None, estimated)
        points.append(point)
        prev_point, prev_window, prev_snapshot, missed = point, window, snap, False
    events += list(pending.values()) + list(expirations.values())
    latest: dict[str, HistoryEvent] = {}
    for e in sorted((e for e in events if start <= e.At <= through), key=lambda e: e.ReportedAt):
        latest[e.Id] = e
    historical = tuple(sorted(latest.values(), key=lambda e: (e.At, e.Id)))
    if not points:
        status = status or "No Recorded History Yet"
    elif through - points[-1].At > max_gap:
        status = status or "Waiting for Fresh Usage"
    return HistorySeries(account, period, plan, tuple(points), historical, status)


def _same_scope(provider: str, a: str, b: str) -> bool:
    return a.lower() == b.lower() if provider == "GitHub Copilot" else a == b


def downsample(points: Sequence[HistoryPoint], project_x: Callable[[datetime], float], min_x: float, max_x: float) -> list[HistoryPoint]:
    """Pixel-bucket reduction for drawing only; keeps first/last/min/max per pixel and estimated-span endpoints."""
    if not points or max_x < min_x:
        return []

    def bound(x: float, inclusive: bool) -> int:
        lo, hi = 0, len(points)
        while lo < hi:
            mid = (lo + hi) // 2
            p = project_x(points[mid].At)
            if p < x or (not inclusive and p == x):
                lo = mid + 1
            else:
                hi = mid
        return lo

    first = max(0, bound(min_x, True) - 1)
    last = min(len(points) - 1, bound(max_x, False))
    if last - first < 2:
        return list(points[first:last + 1])
    keep: set[int] = set()
    bucket = math.nan
    b_first = b_last = lo_i = hi_i = first
    for i in range(first, last + 1):
        x = math.floor(project_x(points[i].At))
        if x != bucket:
            if not math.isnan(bucket):
                keep.update((b_first, b_last, lo_i, hi_i))
            bucket, b_first, lo_i, hi_i = x, i, i, i
        b_last = i
        if points[i].RemainingPercent < points[lo_i].RemainingPercent:
            lo_i = i
        if points[i].RemainingPercent > points[hi_i].RemainingPercent:
            hi_i = i
        p = points[i]
        if (p.StartsSegment or p.ReplenishedFrom is not None or p.InterpolatedFromPrevious
                or (i > first and points[i - 1].InterpolatedFromPrevious)):
            keep.add(i)
            if i > first:
                keep.add(i - 1)
    keep.update((b_first, b_last, lo_i, hi_i))
    return [points[i] for i in sorted(keep)]


# ------------------------------------------------------------------ chart colors

LINE_SWATCHES = ("#B99AF8", "#58A6FF", "#55C9BD", "#CDEB82", "#F2C94C", "#D587A5", "#F05B69")


def account_color(account: ProviderAccount, plan: str) -> str:
    c = account.LineColor
    if c and len(c) == 7 and c[0] == "#" and all(ch in "0123456789abcdefABCDEF" for ch in c[1:]):
        return c
    if account.Provider == "Claude":
        return "#58A6FF"
    if account.Provider == "GitHub Copilot":
        return "#CDEB82"
    if account.Provider == "OpenAI":
        return "#D587A5" if "plus" in plan.lower() else "#B99AF8"
    return color_for_account(account.Id)


def color_for_account(account_id: str) -> str:
    """Stable FNV-1a derived hue so colors survive restarts and reordering."""
    h = 2166136261
    for ch in account_id:
        h = ((h ^ ord(ch)) * 16777619) & 0xFFFFFFFF
    hue = (h & 0xFFFFFF) / 16777216 * 6
    sat = .48 + ((h >> 24) & 0xFF) / 255 * .22
    light = .72
    chroma = (1 - abs(2 * light - 1)) * sat
    sec = chroma * (1 - abs(hue % 2 - 1))
    off = light - chroma / 2
    r, g, b = [(chroma, sec, 0), (sec, chroma, 0), (0, chroma, sec), (0, sec, chroma), (sec, 0, chroma), (chroma, 0, sec)][min(5, int(hue))]
    return "#" + "".join(f"{int(round((v + off) * 255)):02X}" for v in (r, g, b))


# ------------------------------------------------------------------ horizon geometry

def _valid_local(dt: datetime) -> bool:
    return dt.astimezone(timezone.utc).astimezone(dt.tzinfo).replace(tzinfo=None) == dt.replace(tzinfo=None)


def resolve_local(local: datetime, zone: tzinfo) -> datetime:
    """Naive wall time -> aware instant. Skips DST gaps; ambiguous times take the earlier instant."""
    dt = local.replace(tzinfo=zone, fold=0)
    while not _valid_local(dt):
        local += timedelta(minutes=1)
        dt = local.replace(tzinfo=zone, fold=0)
    return dt


def midnight(day: date, zone: tzinfo) -> datetime:
    return resolve_local(datetime.combine(day, time.min), zone)


def local_date(instant: datetime, zone: tzinfo) -> date:
    return instant.astimezone(zone).date()


def coordinate(instant: datetime, zone: tzinfo) -> float:
    """Calendar-day coordinate: equal width per local day, even across DST transitions."""
    day = local_date(instant, zone)
    begin = midnight(day, zone)
    end = midnight(day + timedelta(days=1), zone)
    return day.toordinal() + (instant - begin).total_seconds() / (end - begin).total_seconds()


def from_coordinate(value: float, zone: tzinfo) -> datetime:
    number = math.floor(value)
    day = date.fromordinal(number)
    begin = midnight(day, zone)
    end = midnight(day + timedelta(days=1), zone)
    return (begin + (end - begin) * (value - number)).astimezone(zone)


class HorizonViewport:
    """Calendar-day geometry shared by day headings, rail, history graph, events and navigator."""
    HISTORY_DAYS = 7
    MIN_DAY_WIDTH = 80.0

    def __init__(self, now: datetime, furthest: Optional[datetime], zone: tzinfo, width: float,
                 history: bool = True, earliest_history: Optional[datetime] = None):
        self._initialized = False
        self.following_today = True
        self.scroll_offset = 0.0
        self.update(now, furthest, zone, width, history, earliest_history)

    @property
    def today(self) -> date:
        return local_date(self.now, self.zone)

    @property
    def last_date(self) -> date:
        return local_date(self.last_event_at, self.zone)

    @property
    def visible_days(self) -> float:
        return self.viewport_width / self.pixels_per_day

    @property
    def content_width(self) -> float:
        return (self._end - self._start) * self.pixels_per_day

    @property
    def max_scroll(self) -> float:
        return max(0.0, self.content_width - self.viewport_width)

    @property
    def today_position(self) -> float:
        return self.position_at(self.now)

    @property
    def visible_start(self) -> datetime:
        return self.date_at(self.scroll_offset)

    @property
    def visible_end(self) -> datetime:
        return self.date_at(self.scroll_offset + self.viewport_width)

    def update(self, now: datetime, furthest: Optional[datetime], zone: tzinfo, width: float,
               history: bool = True, earliest_history: Optional[datetime] = None) -> None:
        previous_left = self.visible_start if self._initialized else now
        mode_changed = self._initialized and history != self.history
        self.now, self.zone, self.history = now, zone, history
        self.viewport_width = max(1.0, width) if math.isfinite(width) else 1.0
        self.last_event_at = furthest if furthest is not None and furthest > now else now
        today = self.today
        if history:
            local_now = now.astimezone(zone).replace(tzinfo=None)
            self.start_at = resolve_local(local_now - timedelta(days=self.HISTORY_DAYS), zone)
            if earliest_history is not None and earliest_history > self.start_at:
                self.start_at = min(earliest_history, now)
            self._start = coordinate(self.start_at, zone)
            history_span = coordinate(now, zone) - self._start
            future_span = self.last_date.toordinal() - today.toordinal() + 1
            self.pixels_per_day = max(self.MIN_DAY_WIDTH, self.viewport_width / max(1.0, history_span, future_span))
            self._end = today.toordinal() + max(future_span, self.visible_days)
        else:
            self.start_at = midnight(today, zone)
            self._start = coordinate(self.start_at, zone)
            self._end = coordinate(midnight(self.last_date + timedelta(days=1), zone), zone)
            self.pixels_per_day = max(self.MIN_DAY_WIDTH, self.viewport_width / max(1.0, self._end - self._start))
        self.end_at = from_coordinate(self._end, zone)
        if not self._initialized or mode_changed or self.following_today:
            self.follow_today()
        else:
            self.scroll_offset = self._clamp(self.position_at(previous_left))
        self._initialized = True

    def follow_today(self) -> None:
        self.following_today = True
        self.scroll_offset = self._clamp(self.position_of_date(self.today)) if self.history else 0.0

    def scroll_to(self, offset: float) -> None:
        if math.isnan(offset):
            return
        self.scroll_offset = self._clamp(offset)
        anchor = self._clamp(self.position_of_date(self.today)) if self.history else 0.0
        self.following_today = abs(self.scroll_offset - anchor) < 0.5

    def position_at(self, instant: datetime) -> float:
        return (coordinate(instant, self.zone) - self._start) * self.pixels_per_day

    def position_of_date(self, day: date) -> float:
        return (day.toordinal() - self._start) * self.pixels_per_day

    def day_center(self, day: date) -> float:
        """Equal-width day buckets; headings, dots and grouped icons all anchor here."""
        return self.position_of_date(day) + 0.5 * self.pixels_per_day

    def date_at(self, x: float) -> datetime:
        if not math.isfinite(x):
            x = 0.0
        return from_coordinate(self._start + max(0.0, min(self.content_width, x)) / self.pixels_per_day, self.zone)

    def overview(self, track: float) -> tuple[float, float]:
        track = max(0.0, track)
        return (self.scroll_offset / self.content_width * track,
                min(1.0, self.viewport_width / self.content_width) * track)

    def overview_position(self, instant: datetime, track: float) -> float:
        return self.position_at(instant) / self.content_width * max(0.0, track)

    def center_from_overview(self, pointer_x: float, track: float) -> None:
        if track <= 0 or not math.isfinite(pointer_x):
            return
        self.scroll_to(pointer_x / track * self.content_width - self.viewport_width / 2)

    def drag_overview(self, initial: float, delta: float, track: float) -> None:
        if track <= 0:
            return
        self.scroll_to(initial + delta / track * self.content_width)

    def _clamp(self, offset: float) -> float:
        return max(0.0, min(self.max_scroll, offset))
