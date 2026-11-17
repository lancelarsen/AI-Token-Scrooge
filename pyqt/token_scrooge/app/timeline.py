"""Reset Horizon: one calendar-day timeline for upcoming refreshes, earned-reset expiries and usage history.

Port of HorizonView + HistoryTimelineView. Geometry rules (see AGENTS.md "Reset Horizon Geometry"):
- History on and off use this same renderer. The toggle only adds or removes history content.
- Every calendar day has the same width (`HorizonViewport.pixels_per_day`), including across DST.
- Day headings, rail dots and grouped future-event tiles share one anchor: `day_anchor_x`.
- Historical markers and usage samples sit at their actual timestamps on the same scale.
"""
from __future__ import annotations

import bisect
import math
from datetime import date, datetime, timedelta, timezone, tzinfo
from typing import Callable, Iterable, Optional, Sequence

from PyQt6.QtCore import QEvent, QPointF, QRectF, QTimer, Qt
from PyQt6.QtGui import QColor, QFontMetricsF, QPainter, QPainterPath, QPen, QPixmap
from PyQt6.QtWidgets import (QAbstractButton, QFrame, QGridLayout, QHBoxLayout, QScrollArea, QSizePolicy, QToolTip,
                             QVBoxLayout, QWidget)

from ..core.labels import format_reset, format_time, plan_label, utcnow
from ..core.model import replace, round_away
from . import theme as T
from .presentation import (HistoryPoint, HistorySeries, HorizonEvent, HorizonViewport, account_color, downsample,
                           local_date, midnight)
from .widgets import ActionButton, Card, ToggleSwitch, hline, label

AXIS_LEFT = 46.0
PLOT_RIGHT = 12.0
SELECTOR_HEIGHT = 66
SELECTOR_SPACING = 6
SELECTOR_WIDTH = 88
NAVIGATOR_HEIGHT = 22
TILE_WIDTH, TILE_HEIGHT, TILE_PITCH = 62.0, 74.0, 76.0
REFRESH_MARKER = "#B99AF8"
MARKER_TEXT = "#232429"
LABEL_BG = "#202229"
MUTED = T.TEXT_DETAIL
ANCHOR_DATE = date(2000, 1, 1)
ANCHOR_ORDINAL = ANCHOR_DATE.toordinal()


# ------------------------------------------------------------------ pure geometry helpers

def day_anchor_x(viewport: HorizonViewport, day: date, axis_left: float = AXIS_LEFT) -> float:
    """The single x anchor for a calendar day's heading, rail dot and grouped event tiles."""
    return axis_left + viewport.day_center(day) - viewport.scroll_offset


def day_boundary_x(viewport: HorizonViewport, day: date, axis_left: float = AXIS_LEFT) -> float:
    """Left edge (local midnight) of a calendar-day bucket; history grid lines use this."""
    return axis_left + viewport.position_of_date(day) - viewport.scroll_offset


def chart_height(accounts: int, future_rows: int, rail_y: float) -> float:
    shown = max(3, accounts)
    return max(16 + shown * SELECTOR_HEIGHT + (shown - 1) * SELECTOR_SPACING, rail_y + 21 + max(0, future_rows) * TILE_PITCH)


def place_labels(desired: Sequence[float], top: float, bottom: float, spacing: float = 25) -> list[float]:
    """Places ordered labels inside the graph while preserving their vertical order."""
    if not desired:
        return []
    bottom = max(top, bottom)
    spacing = min(spacing, (bottom - top) / max(1, len(desired) - 1))
    positions = [0.0] * len(desired)
    for i, want in enumerate(desired):
        positions[i] = max(min(max(want, top), bottom), top if i == 0 else positions[i - 1] + spacing)
    positions[-1] = min(positions[-1], bottom)
    for i in range(len(positions) - 2, -1, -1):
        positions[i] = min(positions[i], positions[i + 1] - spacing)
    return positions


def visible_events(events: Iterable[HorizonEvent], settings) -> list[HorizonEvent]:
    return sorted((e for e in events if settings.timeline_includes(e.AccountId)), key=lambda e: e.At)


def _num(value: float) -> str:
    text = f"{round_away(value, 1):.1f}"
    return text[:-2] if text.endswith(".0") else text


class _DayCoordinates:
    """Fast calendar-day coordinates with cached local midnights (same math as presentation.coordinate)."""

    def __init__(self, zone: tzinfo):
        self.zone = zone
        self._bounds: dict[date, tuple[datetime, float]] = {}

    def __call__(self, instant: datetime) -> float:
        day = instant.astimezone(self.zone).date()
        bounds = self._bounds.get(day)
        if bounds is None:
            begin = midnight(day, self.zone)
            bounds = (begin, (midnight(day + timedelta(days=1), self.zone) - begin).total_seconds())
            self._bounds[day] = bounds
        return day.toordinal() + (instant - bounds[0]).total_seconds() / bounds[1]


def point_details(series: HistorySeries, point: HistoryPoint, zone: tzinfo, start_at: Optional[datetime]) -> str:
    lines = [f"{series.Account.Provider} / {series.Account.Nickname} · {series.PlanLabel}",
             f"{series.Period} · {_num(100 - point.RemainingPercent)}% Used",
             format_reset(point.At, zone)]
    coverage = coverage_details(series, zone, start_at)
    if coverage:
        lines.append(coverage)
    if point.InterpolatedFromPrevious:
        lines.append("Estimated Linear Connection From Previous Observation")
    if point.ReplenishedFrom is not None:
        lines.append(f"Capacity Rose From {_num(point.ReplenishedFrom)}% to {_num(point.RemainingPercent)}%")
        lines.append("Observed Between Polls")
        if point.PreviousObservedAt is not None:
            lines.append(f"{format_reset(point.PreviousObservedAt, zone)} – {format_reset(point.At, zone)}")
    return "\n".join(lines)


def coverage_details(series: HistorySeries, zone: tzinfo, start_at: Optional[datetime]) -> str:
    if series.Points and start_at is not None and series.Points[0].At > start_at:
        return f"History Begins {format_reset(series.Points[0].At, zone)}"
    return ""


def _color(value: str, alpha: float = 1.0) -> QColor:
    c = QColor(value)
    c.setAlphaF(alpha)
    return c


def _dashed(color: QColor, width: float, dash: float) -> QPen:
    pen = QPen(color, width)
    pen.setDashPattern([dash / width, dash / width])
    return pen


# ------------------------------------------------------------------ chart canvas

class _Canvas(QWidget):
    """Paints headings, rail, day dots, history graph, historical markers and future tiles in one pass."""

    def __init__(self, owner: "ResetHorizon"):
        super().__init__(owner)
        self.owner = owner
        self.setMouseTracking(True)
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)
        self.setMinimumWidth(220)
        self.setAccessibleName("Reset Horizon Timeline")
        self.setAccessibleDescription("Arrow Keys Inspect Samples · Page Up and Page Down Move Seven Days · "
                                      "Home Returns to Today · Escape Closes Details")
        self.f_day, self.f_date = T.font(14, True), T.font(12)
        self.f_axis, self.f_small, self.f_small_sb = T.font(11), T.font(10), T.font(10, True)
        self.f_readout, self.f_readout_sb, self.f_marker, self.f_status = T.font(12), T.font(12, True), T.font(12, True), T.font(13)
        day_height = QFontMetricsF(self.f_day).height()
        self.heading_height = day_height + 7 + QFontMetricsF(self.f_date).height()
        self.day_label_height = day_height
        # Same 7px stack gap plus the 11px rail half-height the Windows day cell uses.
        self.rail_y = math.ceil(self.heading_height + 18)
        self.series: list[HistorySeries] = []
        self.future: list[HorizonEvent] = []
        self.hidden: set[str] = set()
        self.zone: tzinfo = timezone.utc
        self.now: datetime = utcnow()
        self.history = True
        self.loading = False
        self.error: Optional[str] = None
        self.viewport: Optional[HorizonViewport] = None
        self.colors: dict[str, str] = {}
        self._coords: dict[str, list[float]] = {}
        self._coord_at: dict[datetime, float] = {}
        self._coord_key: object = None
        self.cursor: Optional[tuple[HistorySeries, HistoryPoint]] = None
        self.readout: Optional[tuple[str, QPointF]] = None
        self.pinned = False
        self.tiles: list[tuple[QRectF, HorizonEvent, date, float]] = []
        self.markers: list[tuple[QRectF, bool, int, str]] = []
        self.hover_tile: Optional[QRectF] = None
        self.painted: dict[str, dict[date, float]] = {"headings": {}, "dots": {}, "groups": {}}
        self.navigating = False
        self._drag_offset = self._drag_pointer = 0.0
        self._pixmaps: dict[str, QPixmap] = {}
        self.plot_bottom = 256.0
        self.setFixedHeight(int(chart_height(0, 0, self.rail_y)))

    # ---------------------------------------------------------------- geometry
    @property
    def span(self) -> float:
        return max(1.0, self.width() - AXIS_LEFT - PLOT_RIGHT)

    @property
    def end(self) -> float:
        return AXIS_LEFT + self.span

    @property
    def plot_top(self) -> float:
        return self.rail_y + 32

    @property
    def visible(self) -> list[HistorySeries]:
        return [s for s in self.series if s.Account.Id not in self.hidden]

    @property
    def today_x(self) -> float:
        return AXIS_LEFT + self.viewport.today_position - self.viewport.scroll_offset

    def _origin(self) -> float:
        return self.viewport.position_of_date(ANCHOR_DATE)

    def content_x(self, at: datetime) -> float:
        c = self._coord_at.get(at)
        if c is None:
            return self.viewport.position_at(at)
        return self._origin() + (c - ANCHOR_ORDINAL) * self.viewport.pixels_per_day

    def plot_x(self, at: datetime) -> float:
        return AXIS_LEFT + self.content_x(at) - self.viewport.scroll_offset

    def plot_y(self, remaining: float) -> float:
        return self.plot_bottom - (100 - remaining) / 100 * (self.plot_bottom - self.plot_top)

    def points_within(self, series: HistorySeries, left: float, right: float) -> list[HistoryPoint]:
        coords = self._coords.get(series.Account.Id)
        if not coords:
            return []
        origin, ppd = self._origin(), self.viewport.pixels_per_day
        lo = bisect.bisect_left(coords, ANCHOR_ORDINAL + (left - origin) / ppd)
        hi = bisect.bisect_right(coords, ANCHOR_ORDINAL + (right - origin) / ppd)
        return list(series.Points[lo:hi])

    # ---------------------------------------------------------------- data
    def set_data(self, series: list[HistorySeries], future: list[HorizonEvent], hidden: set[str], zone: tzinfo,
                 now: datetime, history: bool, loading: bool = False, error: Optional[str] = None) -> None:
        if history != self.history:
            self.clear_hover()
        self.series, self.future, self.hidden = series, sorted(future, key=lambda e: e.At), hidden
        self.zone, self.now, self.history, self.loading, self.error = zone, now, history, loading, error
        for s in series:
            # Stable through refresh, selection and dashboard reordering.
            self.colors[s.Account.Id] = account_color(s.Account, s.PlanLabel)
        key = (str(zone), tuple((s.Account.Id, id(s.Points)) for s in series))
        if key != self._coord_key:
            project = _DayCoordinates(zone)
            self._coords.clear()
            self._coord_at.clear()
            for s in series:
                values = [project(p.At) for p in s.Points]
                self._coords[s.Account.Id] = values
                self._coord_at.update(zip((p.At for p in s.Points), values))
            self._coord_key = key
        if self.cursor is not None and self.cursor[0].Account.Id not in {s.Account.Id for s in self.visible}:
            self.clear_hover()
        self.reflow()

    def color_of(self, series: HistorySeries) -> str:
        return self.colors.get(series.Account.Id, T.DEFAULT_LINE)

    def reflow(self) -> None:
        earliest = min((s.Points[0].At for s in self.series if s.Points), default=self.now)
        furthest = self.future[-1].At if self.future else None
        if self.viewport is None:
            self.viewport = HorizonViewport(self.now, furthest, self.zone, self.span, self.history, earliest)
        else:
            self.viewport.update(self.now, furthest, self.zone, self.span, self.history, earliest)
        rows = 0
        counts: dict[date, int] = {}
        for e in self.future:
            day = local_date(e.At, self.zone)
            counts[day] = counts.get(day, 0) + 1
            rows = max(rows, counts[day])
        height = math.ceil(chart_height(len(self.series), rows, self.rail_y))
        self.plot_bottom = height - 8
        if self.height() != height:
            self.setFixedHeight(height)
            self.owner._sync_heights(height)
        self._layout_events()
        self.update()
        self.owner.navigator.update()

    def resizeEvent(self, event):
        super().resizeEvent(event)
        if self.viewport is not None:
            self.reflow()

    def _layout_events(self) -> None:
        vp = self.viewport
        self.tiles = []
        groups: dict[date, list[HorizonEvent]] = {}
        for e in self.future:
            groups.setdefault(local_date(e.At, self.zone), []).append(e)
        for day, items in groups.items():
            x = day_anchor_x(vp, day)
            if x < AXIS_LEFT - 24 or x > self.end + 12:
                continue
            for slot, e in enumerate(items):
                rect = QRectF(x - TILE_WIDTH / 2, self.rail_y + 21 + slot * TILE_PITCH, TILE_WIDTH, TILE_HEIGHT)
                self.tiles.append((rect, e, day, x))
        self.markers = []
        if not self.history:
            return
        items = sorted(((s, e) for s in self.visible for e in s.Events
                        if e.At <= self.now and AXIS_LEFT + 10 <= self.plot_x(e.At) <= self.end - 10),
                       key=lambda item: item[1].At)
        grouped: dict[tuple[datetime, bool], list[tuple[HistorySeries, object]]] = {}
        for s, e in items:
            grouped.setdefault((e.At, e.IsExpiry), []).append((s, e))
        # Keep each event at its actual timestamp, never an average of nearby events.
        lane_ends: list[float] = []
        for (at, expiry), values in grouped.items():
            x = self.plot_x(at)
            lane = next((i for i, right in enumerate(lane_ends) if x - 11 >= right + 2), -1)
            if lane < 0:
                lane = len(lane_ends)
                lane_ends.append(0.0)
            lane_ends[lane] = x + 11
            kind = "Reset Expiration" if expiry else "Quota Refresh"
            details = "\n\n".join(f"{s.Account.Provider} / {s.Account.Nickname} · {s.PlanLabel}\n"
                                  f"{kind}: {format_reset(e.At, self.zone)}\n{e.Detail}" for s, e in values)
            self.markers.append((QRectF(x - 11, self.rail_y - 11 + lane * 24, 22, 22), expiry, len(values), details))

    # ---------------------------------------------------------------- navigation
    def pan(self, days: float) -> None:
        self.pan_pixels(days * self.viewport.pixels_per_day)

    def pan_pixels(self, pixels: float) -> None:
        self.viewport.scroll_to(self.viewport.scroll_offset + pixels)
        self.clear_hover()
        self._layout_events()
        self.update()
        self.owner.navigator.update()

    def follow_today(self) -> None:
        self.viewport.follow_today()
        self.clear_hover()
        self._layout_events()
        self.update()
        self.owner.navigator.update()

    def begin_navigation(self, x: float, track: float) -> None:
        if track <= 0:
            return
        self.navigating = True
        left, width = self.viewport.overview(track)
        if x < left or x > left + width:
            self.viewport.center_from_overview(x, track)
        self._drag_offset, self._drag_pointer = self.viewport.scroll_offset, x
        self.pan_pixels(0)

    def drag_navigation(self, x: float, track: float) -> None:
        if not self.navigating:
            return
        self.viewport.drag_overview(self._drag_offset, x - self._drag_pointer, track)
        self.pan_pixels(0)

    def end_navigation(self) -> None:
        self.navigating = False

    def wheel(self, event) -> None:
        """Wheel and trackpad input always pans the timeline and is never passed to the page."""
        pixel, angle = event.pixelDelta(), event.angleDelta()
        if not pixel.isNull():
            delta = pixel.x() if abs(pixel.x()) > abs(pixel.y()) else pixel.y()
            self.pan_pixels(-delta)
        else:
            delta = angle.x() if abs(angle.x()) > abs(angle.y()) else angle.y()
            if delta:
                self.pan(-delta / 240)
        event.accept()

    # ---------------------------------------------------------------- hover and readout
    def clear_hover(self) -> None:
        self.cursor = None
        self.readout = None
        self.pinned = False
        self.update()

    def details(self, series: HistorySeries, point: HistoryPoint) -> str:
        return point_details(series, point, self.zone, self.viewport.start_at if self.viewport else None)

    def hover(self, x: float, y: float) -> Optional[str]:
        if (not self.history or self.navigating or x < AXIS_LEFT or x > self.end or x > self.today_x
                or y < self.plot_top or y > self.plot_bottom):
            return None
        content = x - AXIS_LEFT + self.viewport.scroll_offset
        best, score = None, math.inf
        for s in self.visible:
            for p in self.points_within(s, content - 18, content + 18):
                value = abs(self.plot_x(p.At) - x) + abs(self.plot_y(p.RemainingPercent) - y) * .45
                if value < score:
                    best, score = (s, p), value
        if best is not None:
            self.cursor = best
            return self.details(*best)
        # A gap has no recorded point near the pointer, but its connecting line is inspectable.
        for s in self.visible:
            coords = self._coords.get(s.Account.Id) or []
            target = ANCHOR_ORDINAL + (content - self._origin()) / self.viewport.pixels_per_day
            index = bisect.bisect_left(coords, target)
            if index == 0 or index >= len(s.Points):
                continue
            before, after = s.Points[index - 1], s.Points[index]
            if not after.InterpolatedFromPrevious or after.StartsSegment:
                continue
            left, right = self.content_x(before.At), self.content_x(after.At)
            fraction = (content - left) / (right - left) if right > left else 0
            remaining = before.RemainingPercent + (after.RemainingPercent - before.RemainingPercent) * fraction
            if abs(self.plot_y(remaining) - y) > 24:
                continue
            at = before.At + (after.At - before.At) * fraction
            estimate = HistoryPoint(at, remaining, after.WindowId, False, before.At, None, True)
            self.cursor = (s, estimate)
            return self.details(s, estimate)
        return None

    def move_cursor(self, direction: int) -> Optional[str]:
        if not self.history:
            return None
        vp = self.viewport
        values = sorted(((s, p) for s in self.visible for p in self.points_within(s, vp.scroll_offset, vp.scroll_offset + self.span)),
                        key=lambda v: v[1].At)
        if not values:
            return None
        current = next((i for i, v in enumerate(values) if self.cursor is not None and v[0].Account.Id == self.cursor[0].Account.Id
                        and v[1] == self.cursor[1]), None)
        index = current if current is not None else (-1 if direction > 0 else len(values))
        self.cursor = values[max(0, min(len(values) - 1, index + direction))]
        return self.details(*self.cursor)

    def _cursor_anchor(self) -> QPointF:
        if self.cursor is None:
            return QPointF(AXIS_LEFT + 20, self.plot_top + 20)
        return QPointF(self.plot_x(self.cursor[1].At), self.plot_y(self.cursor[1].RemainingPercent))

    def _show_readout(self, text: Optional[str], anchor: Optional[QPointF] = None, pinned: bool = False) -> None:
        if text is None:
            self.clear_hover()
            return
        self.readout = (text, anchor if anchor is not None else self._cursor_anchor())
        self.pinned = pinned
        self.update()

    def _tile_at(self, pos: QPointF) -> Optional[tuple[QRectF, HorizonEvent, date, float]]:
        return next((t for t in self.tiles if t[0].contains(pos)), None)

    def _marker_at(self, pos: QPointF) -> Optional[tuple[QRectF, bool, int, str]]:
        return next((m for m in self.markers if m[0].adjusted(-2, -2, 2, 2).contains(pos)), None)

    @staticmethod
    def tile_text(e: HorizonEvent, zone: tzinfo) -> str:
        kind = "Reset Expires" if e.IsExpiry else "Quota Refresh"
        return f"{e.Title}\n{e.Detail}\n{kind}: {format_reset(e.At, zone)}"

    def mouseMoveEvent(self, event):
        pos = event.position()
        tile = self._tile_at(pos)
        marker = None if tile else self._marker_at(pos)
        hover_rect = tile[0] if tile else None
        if hover_rect != self.hover_tile:
            self.hover_tile = hover_rect
            self.update()
        self.setCursor(Qt.CursorShape.PointingHandCursor if tile or marker else Qt.CursorShape.ArrowCursor)
        if self.pinned or tile or marker:
            return
        text = self.hover(pos.x(), pos.y())
        if text is None:
            if self.readout is not None or self.cursor is not None:
                self.clear_hover()
        else:
            self._show_readout(text)

    def mousePressEvent(self, event):
        self.setFocus(Qt.FocusReason.MouseFocusReason)
        if event.button() != Qt.MouseButton.LeftButton:
            return
        pos = event.position()
        tile = self._tile_at(pos)
        if tile is not None:
            self.clear_hover()
            self.owner.open_event(tile[1])
            return
        marker = self._marker_at(pos)
        if marker is not None:
            self.cursor = None
            self._show_readout(marker[3], marker[0].center(), pinned=True)
            return
        text = self.hover(pos.x(), pos.y())
        if text is None:
            self.clear_hover()
        else:
            self._show_readout(text, pinned=True)

    def leaveEvent(self, event):
        if self.hover_tile is not None:
            self.hover_tile = None
            self.update()
        if not self.pinned:
            self.clear_hover()

    def focusOutEvent(self, event):
        super().focusOutEvent(event)
        if event.reason() != Qt.FocusReason.PopupFocusReason:
            self.clear_hover()

    def event(self, event):
        if event.type() == QEvent.Type.ToolTip:
            pos = QPointF(event.pos())
            tile = self._tile_at(pos)
            marker = None if tile else self._marker_at(pos)
            text = self.tile_text(tile[1], self.zone) if tile else marker[3] if marker and not self.pinned else None
            if text:
                QToolTip.showText(event.globalPos(), text, self)
            else:
                QToolTip.hideText()
                event.ignore()
            return True
        return super().event(event)

    def wheelEvent(self, event):
        self.wheel(event)

    def keyPressEvent(self, event):
        key = event.key()
        if key in (Qt.Key.Key_Left, Qt.Key.Key_Up, Qt.Key.Key_Right, Qt.Key.Key_Down):
            direction = -1 if key in (Qt.Key.Key_Left, Qt.Key.Key_Up) else 1
            if self.history:
                self._show_readout(self.move_cursor(direction), pinned=True)
            else:
                self.pan(direction)
        elif key == Qt.Key.Key_PageUp:
            self.pan(-7)
        elif key == Qt.Key.Key_PageDown:
            self.pan(7)
        elif key == Qt.Key.Key_Home:
            self.follow_today()
        elif key in (Qt.Key.Key_Return, Qt.Key.Key_Enter, Qt.Key.Key_Space):
            self._show_readout(self.move_cursor(0), pinned=True)
        elif key == Qt.Key.Key_Escape and (self.readout is not None or self.cursor is not None):
            self.clear_hover()
        else:
            super().keyPressEvent(event)
            return
        event.accept()

    # ---------------------------------------------------------------- painting
    def status_text(self) -> Optional[str]:
        if not self.history:
            return None if self.future else "No Upcoming Events Reported"
        if self.today_x <= AXIS_LEFT + 30:
            return None
        visible = self.visible
        left = self.viewport.scroll_offset
        shown = any(self.points_within(s, left, left + self.span) for s in visible)
        if self.error or (self.loading and all(not s.Points for s in self.series)) or not visible or not shown:
            return self.error or ("Loading History" if self.loading else
                                  "Select an Account to View History" if not visible else "No Recorded Usage in This Period")
        return None

    def _provider_pixmap(self, provider: str) -> QPixmap:
        pm = self._pixmaps.get(provider)
        if pm is None:
            pm = self._pixmaps[provider] = T.provider_pixmap(provider, 27)
        return pm

    def paintEvent(self, event):
        if self.viewport is None:
            return
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setRenderHint(QPainter.RenderHint.TextAntialiasing)
        p.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)
        vp, end, today_x = self.viewport, self.end, self.today_x
        past_end = max(AXIS_LEFT, min(end, today_x))
        history_shown = self.history and past_end > AXIS_LEFT + 2
        painted = {"headings": {}, "dots": {}, "groups": {}}

        if history_shown:
            p.setFont(self.f_axis)
            for percent in range(0, 101, 25):
                y = self.plot_y(100 - percent)
                p.setPen(QPen(QColor(T.GRID_PERCENT), 1))
                p.drawLine(QPointF(AXIS_LEFT, y), QPointF(past_end, y))
                p.setPen(QColor(MUTED))
                p.drawText(QRectF(12, y - 8, AXIS_LEFT - 16, 16),
                           Qt.AlignmentFlag.AlignRight | Qt.AlignmentFlag.AlignVCenter, f"{percent}%")
            p.save()
            p.translate(7, (self.plot_top + self.plot_bottom) / 2)
            p.rotate(-90)
            p.setFont(self.f_small)
            p.drawText(QRectF(-70, -7, 140, 16), Qt.AlignmentFlag.AlignCenter, "% Used")
            p.restore()

        # Rail across the whole track.
        p.setPen(QPen(QColor(T.RAIL), 2))
        p.drawLine(QPointF(AXIS_LEFT, self.rail_y), QPointF(end, self.rail_y))

        event_days = {local_date(e.At, self.zone) for e in self.future}
        first = local_date(vp.visible_start, self.zone)
        today, last = vp.today, vp.last_date
        day = first
        while day <= last + timedelta(days=7):
            boundary = day_boundary_x(vp, day)
            if boundary > end + vp.pixels_per_day:
                break
            if history_shown and AXIS_LEFT <= boundary <= past_end:
                p.setPen(QPen(QColor(T.GRID_DAY), 1))
                p.drawLine(QPointF(boundary, self.plot_top), QPointF(boundary, self.plot_bottom))
            x = day_anchor_x(vp, day)
            if AXIS_LEFT <= x <= end and day <= last:
                self._paint_heading(p, day, x, day == today)
                painted["headings"][day] = x
                # Both modes use these same circles: 16px Today, 10px other days.
                has_events = day in event_days
                p.setPen(Qt.PenStyle.NoPen)
                p.setBrush(QColor(T.TODAY if day == today else T.ACCENT if has_events else T.CARD))
                radius = 8 if day == today else 5
                p.drawEllipse(QPointF(x, self.rail_y), radius, radius)
                if not has_events and day != today:
                    p.setPen(QPen(QColor(T.ACCENT), 2))
                    p.setBrush(Qt.BrushStyle.NoBrush)
                    p.drawEllipse(QPointF(x, self.rail_y), 4, 4)
                painted["dots"][day] = x
            day += timedelta(days=1)

        if self.history:
            self._paint_history(p, past_end)
            if AXIS_LEFT - 1 <= today_x <= end + 1:
                p.setPen(_dashed(QColor(T.ACCENT), 1.5, 4))
                p.drawLine(QPointF(today_x, self.plot_top), QPointF(today_x, self.plot_bottom))
            self._paint_latest(p, today_x)

        for rect, expiry, count, _ in self.markers:
            p.setPen(Qt.PenStyle.NoPen)
            p.setBrush(QColor(T.EXPIRY if expiry else REFRESH_MARKER))
            p.drawEllipse(rect.center(), 11, 11)
            if count > 1:
                p.setFont(self.f_marker)
                p.setPen(QColor(MARKER_TEXT))
                p.drawText(rect, Qt.AlignmentFlag.AlignCenter, str(count))
            else:
                T.draw_icon(p, "clock" if expiry else "refresh", rect.adjusted(4, 4, -4, -4), MARKER_TEXT, 2.6)

        for rect, e, day, x in self.tiles:
            self._paint_tile(p, rect, e)
            painted["groups"][day] = x

        status = self.status_text()
        if status:
            p.setFont(self.f_status)
            p.setPen(QColor(MUTED))
            if self.history:
                area = QRectF(AXIS_LEFT, self.plot_top, max(1.0, past_end - AXIS_LEFT), self.plot_bottom - self.plot_top)
            else:
                area = QRectF(AXIS_LEFT, self.rail_y + 21, self.span, self.plot_bottom - self.rail_y - 21)
            p.drawText(area, Qt.AlignmentFlag.AlignCenter | Qt.TextFlag.TextWordWrap, status)

        if self.readout is not None:
            self._paint_readout(p, *self.readout)
        self.painted = painted
        p.end()

    def _paint_heading(self, p: QPainter, day: date, x: float, today: bool) -> None:
        width = self.viewport.pixels_per_day
        p.setFont(self.f_day)
        p.setPen(QColor(T.TODAY if today else T.ACCENT))
        p.drawText(QRectF(x - width / 2, 0, width, self.day_label_height), Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop,
                   "Today" if today else day.strftime("%a"))
        p.setFont(self.f_date)
        p.setPen(QColor(MUTED))
        p.drawText(QRectF(x - width / 2, self.day_label_height + 7, width, self.heading_height - self.day_label_height - 7),
                   Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop, f"{day:%b} {day.day}")

    def _paint_history(self, p: QPainter, past_end: float) -> None:
        vp = self.viewport
        p.save()
        p.setClipRect(QRectF(AXIS_LEFT, self.plot_top, self.span, self.plot_bottom - self.plot_top))
        left = vp.scroll_offset
        for s in self.visible:
            color = QColor(self.color_of(s))
            fill = _color(self.color_of(s), .09)
            points = downsample(s.Points, self.content_x, left, left + self.span)
            segment: list[tuple[QPointF, bool]] = []  # (point, drawn dashed from the previous point)

            def flush():
                if not segment:
                    return
                area = QPainterPath(QPointF(segment[0][0].x(), self.plot_bottom))
                for point, _ in segment:
                    area.lineTo(point)
                area.lineTo(QPointF(segment[-1][0].x(), self.plot_bottom))
                area.closeSubpath()
                p.setPen(Qt.PenStyle.NoPen)
                p.setBrush(fill)
                p.drawPath(area)
                p.setBrush(Qt.BrushStyle.NoBrush)
                # Solid for observed spans, dashed for estimated (interpolated) connections.
                run = [segment[0][0]]
                dashed = segment[1][1] if len(segment) > 1 else False
                for point, style in segment[1:]:
                    if style != dashed:
                        self._stroke(p, run, color, dashed)
                        run = [run[-1]]
                        dashed = style
                    run.append(point)
                self._stroke(p, run, color, dashed)
                if len(segment) == 1:
                    p.setPen(Qt.PenStyle.NoPen)
                    p.setBrush(color)
                    p.drawEllipse(segment[0][0], 2.5, 2.5)
                segment.clear()

            for point in points:
                if point.StartsSegment:
                    flush()
                x, y = self.plot_x(point.At), self.plot_y(point.RemainingPercent)
                if point.ReplenishedFrom is not None and segment:
                    segment.append((QPointF(x, self.plot_y(point.ReplenishedFrom)), False))
                segment.append((QPointF(x, y), point.InterpolatedFromPrevious and bool(segment)))
            flush()
        for s in self.visible:
            for e in s.Events:
                x = self.plot_x(e.At)
                if x < AXIS_LEFT or x > past_end:
                    continue
                p.setPen(_dashed(_color(T.EXPIRY if e.IsExpiry else REFRESH_MARKER, .6), 1, 4))
                p.drawLine(QPointF(x, self.plot_top), QPointF(x, self.plot_bottom))
        if self.cursor is not None:
            s, point = self.cursor
            x, y = self.plot_x(point.At), self.plot_y(point.RemainingPercent)
            p.setPen(_dashed(_color(MUTED, .6), 1, 3))
            p.drawLine(QPointF(x, self.plot_top), QPointF(x, self.plot_bottom))
            p.setPen(QPen(QColor("#FFFFFF"), 1))
            p.setBrush(QColor(self.color_of(s)))
            p.drawEllipse(QPointF(x, y), 4, 4)
        p.restore()

    @staticmethod
    def _stroke(p: QPainter, run: list[QPointF], color: QColor, dashed: bool) -> None:
        if len(run) < 2:
            return
        pen = _dashed(color, 2, 5) if dashed else QPen(color, 2)
        pen.setJoinStyle(Qt.PenJoinStyle.RoundJoin)
        p.setPen(pen)
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawPolyline(run)

    def _paint_latest(self, p: QPainter, today_x: float) -> None:
        vp, end = self.viewport, self.end
        latest = []
        for s in self.visible:
            within = self.points_within(s, vp.scroll_offset, vp.scroll_offset + self.span)
            if within:
                latest.append((s, within[-1]))
        latest.sort(key=lambda v: v[1].RemainingPercent)
        positions = place_labels([self.plot_y(v[1].RemainingPercent) - 10 for v in latest], self.plot_top, self.plot_bottom - 21)
        p.setFont(self.f_small)
        for (s, point), label_y in zip(latest, positions):
            x, y = self.plot_x(point.At), self.plot_y(point.RemainingPercent)
            color = QColor(self.color_of(s))
            p.setPen(Qt.PenStyle.NoPen)
            p.setBrush(color)
            p.drawEllipse(QPointF(x, y), 3, 3)
            if today_x < end - 80 or x < end - 70:
                continue
            label_x = end - 86
            p.setPen(QPen(_color(self.color_of(s), .6), 1))
            p.drawLine(QPointF(x - 4, y), QPointF(label_x + 80, label_y + 10))
            box = QRectF(label_x, label_y, 80, 21)
            p.setPen(QPen(color, 1))
            p.setBrush(QColor(LABEL_BG))
            p.drawRoundedRect(box, 6, 6)
            p.setPen(color)
            p.drawText(box.adjusted(3, 0, -3, 0), Qt.AlignmentFlag.AlignCenter,
                       f"{s.PlanLabel} {_num(100 - point.RemainingPercent)}%")

    def _paint_tile(self, p: QPainter, rect: QRectF, e: HorizonEvent) -> None:
        accent = T.EXPIRY if e.IsExpiry else T.ACCENT
        cx, top = rect.center().x(), rect.top()
        tile = QRectF(cx - 21, top, 42, 42)
        hovered = self.hover_tile is not None and self.hover_tile == rect
        p.setPen(QPen(QColor(T.HIGHLIGHT if hovered else accent), 2.5 if hovered else 2))
        p.setBrush(QColor(T.EXPIRY_TILE if e.IsExpiry else T.REFRESH_TILE))
        p.drawRoundedRect(tile.adjusted(1, 1, -1, -1), 12, 12)
        pm = self._provider_pixmap(e.Provider or "OpenAI")
        p.drawPixmap(QRectF(cx - 13.5, top + 7.5, 27, 27), pm, QRectF(pm.rect()))
        if e.PlanBadge:
            text = "?" if e.PlanBadge == "Plan unavailable" else e.PlanBadge
            p.setFont(self.f_small_sb)
            fm = QFontMetricsF(self.f_small_sb)
            width, height = fm.horizontalAdvance(text) + 10, fm.height() + 2
            badge = QRectF(cx - width / 2, top + 49 - height, width, height)
            p.setPen(QPen(QColor(accent), 1))
            p.setBrush(QColor(T.CARD))
            p.drawRoundedRect(badge, 6, 6)
            p.setPen(QColor(accent))
            p.drawText(badge, Qt.AlignmentFlag.AlignCenter, text)
        p.setFont(self.f_small)
        p.setPen(QColor(accent))
        p.drawText(QRectF(cx - 41, top + 52, 82, 18), Qt.AlignmentFlag.AlignHCenter | Qt.AlignmentFlag.AlignTop,
                   format_time(e.At, self.zone))

    def _paint_readout(self, p: QPainter, text: str, anchor: QPointF) -> None:
        lines = text.split("\n")
        bold, regular = QFontMetricsF(self.f_readout_sb), QFontMetricsF(self.f_readout)
        line_height = regular.height() + 2
        width = min(max(bold.horizontalAdvance(lines[0]), *(regular.horizontalAdvance(line) for line in lines)) + 24,
                    max(160.0, self.width() - 16))
        height = len(lines) * line_height + 16
        x = anchor.x() + 14
        if x + width > self.width() - 4:
            x = anchor.x() - 14 - width
        x = max(4.0, min(self.width() - width - 4, x))
        y = max(4.0, min(self.height() - height - 4, anchor.y() - height / 2))
        box = QRectF(x, y, width, height)
        p.setPen(QPen(QColor(T.POPUP_BORDER), 1))
        p.setBrush(QColor(T.DIALOG))
        p.drawRoundedRect(box, 8, 8)
        for i, line in enumerate(lines):
            p.setFont(self.f_readout_sb if i == 0 else self.f_readout)
            p.setPen(QColor(T.TEXT if i == 0 else MUTED))
            p.drawText(QRectF(x + 12, y + 8 + i * line_height, width - 24, line_height),
                       Qt.AlignmentFlag.AlignLeft | Qt.AlignmentFlag.AlignVCenter, line)


# ------------------------------------------------------------------ navigator

class _Navigator(QWidget):
    """Overview track spanning the full width; the thumb drags and an outside click recenters."""

    def __init__(self, owner: "ResetHorizon"):
        super().__init__(owner)
        self.owner = owner
        self.setFixedHeight(NAVIGATOR_HEIGHT)
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setAccessibleName("Timeline Navigator · Drag to Browse Dates")
        self.setToolTip("Drag to Browse Dates")

    @property
    def track(self) -> float:
        return max(1.0, self.width() - 2.0)

    def mousePressEvent(self, event):
        if event.button() == Qt.MouseButton.LeftButton and self.owner.canvas.viewport is not None:
            self.setFocus(Qt.FocusReason.MouseFocusReason)
            self.owner.canvas.begin_navigation(event.position().x() - 1, self.track)

    def mouseMoveEvent(self, event):
        if event.buttons() & Qt.MouseButton.LeftButton:
            self.owner.canvas.drag_navigation(event.position().x() - 1, self.track)

    def mouseReleaseEvent(self, event):
        self.owner.canvas.end_navigation()

    def wheelEvent(self, event):
        if self.owner.canvas.viewport is not None:
            self.owner.canvas.wheel(event)
        event.accept()

    def keyPressEvent(self, event):
        canvas = self.owner.canvas
        key = event.key()
        if canvas.viewport is None:
            return super().keyPressEvent(event)
        if key in (Qt.Key.Key_Left, Qt.Key.Key_Up):
            canvas.pan(-1)
        elif key in (Qt.Key.Key_Right, Qt.Key.Key_Down):
            canvas.pan(1)
        elif key == Qt.Key.Key_PageUp:
            canvas.pan(-7)
        elif key == Qt.Key.Key_PageDown:
            canvas.pan(7)
        elif key == Qt.Key.Key_Home:
            canvas.follow_today()
        else:
            return super().keyPressEvent(event)
        event.accept()

    def paintEvent(self, event):
        canvas = self.owner.canvas
        vp = canvas.viewport
        if vp is None:
            return
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        width = self.track
        left, thumb = vp.overview(width)
        p.setPen(QPen(QColor(T.ACTION_BORDER), 1))
        p.setBrush(QColor(T.ACTION))
        p.drawRoundedRect(QRectF(1, 1, width, 20), 5, 5)
        p.setPen(QPen(QColor(T.ACCENT), 1.5 if self.hasFocus() else 1))
        p.setBrush(QColor(T.INTERACTION))
        p.drawRoundedRect(QRectF(left + 1, 1, thumb, 20), 5, 5)
        groups: dict[tuple[date, bool, bool], datetime] = {}
        for e in canvas.future:
            groups.setdefault((local_date(e.At, canvas.zone), e.IsExpiry, True), e.At)
        if canvas.history:
            for s in canvas.visible:
                for e in s.Events:
                    groups.setdefault((local_date(e.At, canvas.zone), e.IsExpiry, False), e.At)
        p.setPen(Qt.PenStyle.NoPen)
        for (day, expiry, future), at in groups.items():
            position = vp.day_center(day) if future else vp.position_at(at)
            x = 1 + position / vp.content_width * width
            p.setBrush(QColor(T.EXPIRY if expiry else T.ACCENT))
            p.drawEllipse(QPointF(x, 14 if expiry else 9), 2.5, 2.5)
        today = 1 + vp.overview_position(canvas.now, width)
        p.setPen(QPen(QColor(T.NAV_TODAY), 1))
        p.drawLine(QPointF(today, 3), QPointF(today, 19))
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(QColor(T.TODAY))
        center = left + 1 + thumb / 2
        for dx in (-1, 0, 1):
            for dy in (0, 1):
                p.drawEllipse(QPointF(center + dx * 4, 9 + dy * 4), 1, 1)
        p.end()


# ------------------------------------------------------------------ service selectors

class _Selector(QAbstractButton):
    """Checkable service selector: logo tile, nickname badge, plan badge and a check box."""

    def __init__(self, series: HistorySeries, color: str, enabled: bool, tooltip: str, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.series, self.color, self.included = series, color, enabled
        self.setCheckable(True)
        self.setChecked(enabled)
        self.setFixedSize(SELECTOR_WIDTH, SELECTOR_HEIGHT)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setAttribute(Qt.WidgetAttribute.WA_Hover)
        account = series.Account
        self.setToolTip(tooltip)
        self.setAccessibleName(f"{'Hide' if enabled else 'Show'} {account.Provider} / {account.Nickname} · {series.PlanLabel} · Timeline")
        self._logo = T.provider_pixmap(account.Provider, 30)
        self._f_nick, self._f_plan = T.font(11), T.font(10)

    def _badge(self, p: QPainter, text: str, font, rect_for: Callable[[float, float], QRectF]) -> None:
        fm = QFontMetricsF(font)
        accent = _color(self.color, 1 if self.included else .5)
        width = min(SELECTOR_WIDTH, fm.horizontalAdvance(text) + 12)
        rect = rect_for(width, fm.height() + 4)
        p.setPen(QPen(accent, 1.5))
        p.setBrush(QColor(T.ACTION))
        p.drawRoundedRect(rect.adjusted(.75, .75, -.75, -.75), 7, 7)
        p.setFont(font)
        p.setPen(QColor("#FFFFFF" if self.included else "#919195"))
        p.drawText(rect.adjusted(5, 0, -5, 0), Qt.AlignmentFlag.AlignCenter,
                   fm.elidedText(text, Qt.TextElideMode.ElideRight, rect.width() - 10))

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setRenderHint(QPainter.RenderHint.SmoothPixmapTransform)
        ox = (SELECTOR_WIDTH - 82) / 2
        accent = QColor(self.color)
        tile = QRectF(ox + 4, 10, 74, 46)
        p.save()
        p.setOpacity(1 if self.included else .5)
        hovered = self.underMouse() or self.hasFocus()
        p.setPen(QPen(QColor(T.HIGHLIGHT) if hovered else accent, 2.5 if hovered else 2))
        p.setBrush(QColor(T.INTERACTION if self.underMouse() else T.SELECTOR_BG))
        p.drawRoundedRect(tile.adjusted(1, 1, -1, -1), 13, 13)
        p.drawPixmap(QRectF(tile.center().x() - 15, tile.center().y() - 15, 30, 30), self._logo, QRectF(self._logo.rect()))
        p.restore()
        s = self.series
        self._badge(p, s.Account.Nickname, self._f_nick, lambda w, h: QRectF((SELECTOR_WIDTH - w) / 2, 0, w, h))
        plan = s.Period if s.PlanLabel == "Plan unavailable" else s.PlanLabel
        self._badge(p, plan, self._f_plan, lambda w, h: QRectF(ox, SELECTOR_HEIGHT - h, min(w, 60), h))
        box = QRectF(ox + 82 - 19, SELECTOR_HEIGHT - 19, 19, 19)
        p.setPen(QPen(accent, 1.5))
        p.setBrush(accent if self.included else QColor(T.ACTION))
        p.drawRoundedRect(box.adjusted(.75, .75, -.75, -.75), 5, 5)
        if self.included:
            T.draw_icon(p, "check", box.adjusted(3.5, 3.5, -3.5, -3.5), "#FFFFFF", 3.2)
        p.end()


class _HeaderIcon(QWidget):
    def __init__(self, name: str, color: str, size: int, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._name, self._color = name, color
        self.setFixedSize(size, size)

    def paintEvent(self, event):
        p = QPainter(self)
        T.draw_icon(p, self._name, QRectF(self.rect()).adjusted(1, 1, -1, -1), self._color, 2)
        p.end()


class _Dot(QWidget):
    def __init__(self, color: str, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._color = color
        self.setFixedSize(10, 10)

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(QColor(self._color))
        p.drawEllipse(QRectF(0, 0, 10, 10))
        p.end()


# ------------------------------------------------------------------ card

class ResetHorizon(Card):
    """Dashboard card. `reload()` re-reads history (debounced); `tick()` only advances the clock."""

    def __init__(self, controller, open_event: Callable[[HorizonEvent], None], open_settings: Callable[[], None],
                 parent: Optional[QWidget] = None, clock: Optional[Callable[[], datetime]] = None):
        super().__init__(15, parent)
        self.controller = controller
        self.open_event = open_event
        self._clock = clock or utcnow
        self._series: Optional[list[HistorySeries]] = None
        self._loading = False
        self._generation = 0
        self._selector_signature: object = None
        self._rail_width = 112

        outer = QVBoxLayout(self)
        outer.setContentsMargins(24, 18, 24, 18)
        outer.setSpacing(16)
        header = QHBoxLayout()
        header.setSpacing(0)
        header.addWidget(_HeaderIcon("history", T.ACCENT, 32))
        header.addSpacing(12)
        header.addWidget(label("Reset Horizon", 26, T.TEXT, True))
        header.addSpacing(22)
        self.refresh_label = label("Quota Refresh (0)", 13, T.TEXT_DETAIL)
        self.expiry_label = label("Reset Expires (0)", 13, T.TEXT_DETAIL)
        for text, color in ((self.refresh_label, T.ACCENT), (self.expiry_label, T.EXPIRY)):
            header.addWidget(_Dot(color))
            header.addSpacing(7)
            header.addWidget(text)
            header.addSpacing(20)
        self.history_toggle = ToggleSwitch(True)
        self.history_toggle.setAccessibleName("Show Usage History Graph")
        self.history_toggle.setToolTip("Show Usage History Graph")
        self.history_toggle.toggled_by_user.connect(self._toggle_history)
        header.addWidget(self.history_toggle)
        header.addSpacing(7)
        header.addWidget(label("History", 13, T.TEXT))
        header.addSpacing(18)
        self.hint = label("Scroll Left for History", 13, T.TEXT_DETAIL)
        self.hint.setSizePolicy(QSizePolicy.Policy.Ignored, QSizePolicy.Policy.Preferred)
        self.hint.setMinimumWidth(0)
        header.addWidget(self.hint, 1)
        self.settings_button = ActionButton("Settings", "settings", icon_only=True)
        self.settings_button.setAccessibleDescription("Earned Reset Alert Settings")
        self.settings_button.clicked.connect(open_settings)
        header.addWidget(self.settings_button, 0, Qt.AlignmentFlag.AlignTop)
        outer.addLayout(header)
        outer.addWidget(hline())

        grid = QGridLayout()
        grid.setHorizontalSpacing(10)
        grid.setVerticalSpacing(12)
        self.rail = QWidget()
        rail_layout = QHBoxLayout(self.rail)
        rail_layout.setContentsMargins(0, 0, 0, 0)
        rail_layout.setSpacing(0)
        self.selector_scroll = QScrollArea()
        self.selector_scroll.setFrameShape(QFrame.Shape.NoFrame)
        self.selector_scroll.setWidgetResizable(True)
        self.selector_scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.selector_scroll.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        self.selector_scroll.setStyleSheet("QScrollArea { background: transparent; border: none; }")
        self.selector_scroll.viewport().setAutoFillBackground(False)
        self.selector_scroll.viewport().setStyleSheet(f"background: {T.CARD};")
        holder = QWidget()
        holder.setAutoFillBackground(False)
        holder.setStyleSheet(f"background: {T.CARD};")
        self.selector_layout = QVBoxLayout(holder)
        self.selector_layout.setContentsMargins(2, 8, 2, 0)
        self.selector_layout.setSpacing(SELECTOR_SPACING)
        self.selector_layout.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignHCenter)
        self.selector_scroll.setWidget(holder)
        rail_layout.addWidget(self.selector_scroll, 1)
        divider = QWidget()
        divider_layout = QVBoxLayout(divider)
        divider_layout.setContentsMargins(0, 6, 0, 6)
        line = QFrame()
        line.setFixedWidth(1)
        line.setStyleSheet(f"background: {T.CARD_BORDER}; border: none;")
        divider_layout.addWidget(line)
        rail_layout.addWidget(divider)
        self.rail.setFixedWidth(self._rail_width)
        self.navigator = _Navigator(self)
        self.canvas = _Canvas(self)
        grid.addWidget(self.rail, 0, 0)
        grid.addWidget(self.canvas, 0, 1)
        grid.addWidget(self.navigator, 1, 0, 1, 2)
        grid.setColumnStretch(1, 1)
        outer.addLayout(grid)
        self._sync_heights(self.canvas.height())

        self._debounce = QTimer(self)
        self._debounce.setSingleShot(True)
        self._debounce.setInterval(300)
        self._debounce.timeout.connect(self._load)
        self._apply()

    # ---------------------------------------------------------------- public API
    def reload(self) -> None:
        """Show current upcoming events immediately, then re-read history after a short debounce."""
        self._loading = True
        self._apply()
        self._debounce.start()

    def tick(self) -> None:
        """Timestamp-only update: advances Today and drops passed events without reading history."""
        self._apply()

    # ---------------------------------------------------------------- internals
    def _load(self) -> None:
        self._generation += 1
        generation = self._generation
        self._loading = True
        self.controller.load_history(lambda series: self._loaded(generation, series))

    def _loaded(self, generation: int, series: list[HistorySeries]) -> None:
        if generation != self._generation:
            return  # A newer reload superseded this result.
        try:
            self._series = list(series)
            self._loading = False
            self._apply()
        except RuntimeError:
            pass  # Widget already deleted.

    def set_series(self, series: list[HistorySeries]) -> None:
        """Adopt history directly (used by tests and synchronous callers)."""
        self._generation += 1
        self._loaded(self._generation, series)

    def _current_series(self) -> list[HistorySeries]:
        state = self.controller.state
        loaded = {s.Account.Id: s for s in self._series or ()}
        result = []
        for account in state.accounts:
            existing = loaded.get(account.Id)
            if existing is not None:
                # Removed accounts disappear; renamed or recolored ones show their current settings.
                result.append(existing if existing.Account == account else replace(existing, Account=account))
            else:
                snapshot = state.snapshots.get(account.Id)
                period = "Monthly" if account.Provider in ("Devin", "GitHub Copilot") else "Weekly"
                result.append(HistorySeries(account, period, plan_label(account.Provider, snapshot.PlanType if snapshot else None),
                                            (), (), None))
        return result

    def _apply(self) -> None:
        now = self._clock()
        settings = self.controller.settings
        zone = self.controller.zone
        events = visible_events(self.controller.state.events(now), settings)
        self.refresh_label.setText(f"Quota Refresh ({sum(1 for e in events if not e.IsExpiry)})")
        self.expiry_label.setText(f"Reset Expires ({sum(1 for e in events if e.IsExpiry)})")
        history = settings.HistoryEnabled
        self.history_toggle.setChecked(history)
        self.hint.setText("Scroll Left for History" if history else "Select Services to Filter Timeline")
        series = self._current_series()
        hidden = set(settings.HiddenTimelineAccounts or ())
        self._build_selectors(series, hidden)
        self.canvas.set_data(series, events, hidden, zone, now, history, self._loading)

    def _build_selectors(self, series: list[HistorySeries], hidden: set[str]) -> None:
        vp = self.canvas.viewport
        start = vp.start_at if vp else None
        zone = self.controller.zone
        signature = tuple((s.Account.Id, s.Account.Nickname, s.PlanLabel, s.Account.LineColor, s.Account.Id in hidden, s.Status,
                           coverage_details(s, zone, start)) for s in series)
        if signature == self._selector_signature:
            return
        self._selector_signature = signature
        while self.selector_layout.count():
            item = self.selector_layout.takeAt(0)
            if item.widget() is not None:
                item.widget().deleteLater()
        for s in series:
            enabled = s.Account.Id not in hidden
            coverage = coverage_details(s, zone, start)
            tooltip = (f"{s.Account.Provider} / {s.Account.Nickname} · {s.PlanLabel}\nShow or Hide History, Resets and Refreshes"
                       + (f"\n{coverage}" if coverage else "") + (f"\n{s.Status}" if s.Status else ""))
            selector = _Selector(s, account_color(s.Account, s.PlanLabel), enabled, tooltip)
            selector.clicked.connect(lambda _=False, account_id=s.Account.Id, on=enabled: self._select(account_id, not on))
            self.selector_layout.addWidget(selector, 0, Qt.AlignmentFlag.AlignHCenter)

    def _select(self, account_id: str, visible: bool) -> None:
        try:
            self.controller.set_timeline_account(account_id, visible)
        except Exception:
            pass  # Persisting failed: keep the previous selection visible.
        self.canvas.clear_hover()
        self._apply()

    def _toggle_history(self, enabled: bool) -> None:
        try:
            self.controller.set_history_enabled(enabled)
        except Exception:
            pass  # _apply below restores the persisted state.
        self._apply()

    def _sync_heights(self, height: int) -> None:
        self.rail.setFixedHeight(height)

    def resizeEvent(self, event):
        super().resizeEvent(event)
        width = 95 if self.width() < 850 else 112
        if width != self._rail_width:
            self._rail_width = width
            self.rail.setFixedWidth(width)
