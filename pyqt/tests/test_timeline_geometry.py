"""Reset Horizon geometry regressions: equal calendar-day spacing and shared day anchors in the rendered widget."""
import os

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

from datetime import date, datetime, timedelta, timezone
from zoneinfo import ZoneInfo

import pytest
from PyQt6.QtCore import QPoint, QPointF, Qt
from PyQt6.QtGui import QWheelEvent
from PyQt6.QtWidgets import QApplication

from token_scrooge.app.controller import AppController
from token_scrooge.app.presentation import HorizonViewport, build_history, demo_history, local_date
from token_scrooge.app.timeline import ResetHorizon, day_anchor_x, place_labels
from token_scrooge.core.model import replace
from token_scrooge.infra.store import LocalStore

CENTRAL = ZoneInfo("America/Chicago")
NOW = datetime(2026, 11, 17, 15, 40, tzinfo=timezone.utc)


@pytest.fixture(scope="module")
def app():
    return QApplication.instance() or QApplication([])


class DemoController(AppController):
    """Real controller in demo mode with a fixed clock and synchronous history."""

    def __init__(self, tmp_path, now, zone_id="America/Chicago"):
        super().__init__(LocalStore(tmp_path / "state.db"), None, tmp_path)
        self.fixed_now = now
        self.state.settings = replace(self.state.settings, TimeZoneId=zone_id)
        self.state.load_demo(now)

    def load_history(self, done):
        samples = demo_history(self.state, self.fixed_now)
        done([build_history(a, samples.get(a.Id, []), self.fixed_now - timedelta(days=7), self.fixed_now, timedelta(hours=1))
              for a in self.state.accounts])


def make_widget(tmp_path, now=NOW, zone_id="America/Chicago", size=(1200, 420)):
    controller = DemoController(tmp_path, now, zone_id)
    opened = []
    widget = ResetHorizon(controller, opened.append, lambda: None, clock=lambda: now)
    widget.resize(*size)
    widget._load()
    widget.grab()  # Lay out and paint once so the recorded anchors are the rendered ones.
    return widget, controller, opened


def assert_equal_spacing(anchors: dict, pixels_per_day: float):
    days = sorted(anchors)
    assert len(days) >= 2
    for before, after in zip(days, days[1:]):
        assert (after - before).days == 1, "Visible days must be consecutive"
        assert anchors[after] - anchors[before] == pytest.approx(pixels_per_day, abs=1e-6), (before, after)


@pytest.mark.parametrize("now", [datetime(2026, 11, 1, 12, tzinfo=CENTRAL), datetime(2026, 3, 8, 12, tzinfo=CENTRAL)])
@pytest.mark.parametrize("history", [True, False])
def test_day_anchors_equally_spaced_across_dst(now, history):
    view = HorizonViewport(now, now + timedelta(days=12), CENTRAL, 900, history=history)
    for offset in (view.scroll_offset, 0.0, view.max_scroll):
        view.scroll_to(offset)
        first = local_date(view.date_at(0), CENTRAL) - timedelta(days=1)
        anchors = {first + timedelta(days=i): day_anchor_x(view, first + timedelta(days=i)) for i in range(22)}
        assert_equal_spacing(anchors, view.pixels_per_day)
    # The transition day itself is exactly one day wide on the timestamp scale too.
    today = local_date(now, CENTRAL)
    start = datetime.combine(today, datetime.min.time(), CENTRAL)
    end = datetime.combine(today + timedelta(days=1), datetime.min.time(), CENTRAL)
    assert view.position_at(end) - view.position_at(start) == pytest.approx(view.pixels_per_day, abs=1e-6)
    assert day_anchor_x(view, today) - 46 + view.scroll_offset == pytest.approx(
        (view.position_at(start) + view.position_at(end)) / 2, abs=1e-6)


def test_rendered_headings_dots_and_groups_share_anchors(app, tmp_path):
    widget, _, _ = make_widget(tmp_path)
    canvas = widget.canvas
    painted = canvas.painted
    assert painted["headings"] and painted["groups"]
    assert painted["headings"] == painted["dots"]
    assert_equal_spacing(painted["headings"], canvas.viewport.pixels_per_day)
    for day, x in painted["groups"].items():
        if day in painted["headings"]:
            assert x == pytest.approx(painted["headings"][day], abs=1e-9)
        assert x == pytest.approx(day_anchor_x(canvas.viewport, day), abs=1e-9)
    for rect, event, day, x in canvas.tiles:
        assert rect.center().x() == pytest.approx(x, abs=1e-9)
        assert local_date(event.At, canvas.zone) == day
    today = canvas.viewport.today
    assert painted["headings"][today] == pytest.approx(day_anchor_x(canvas.viewport, today))


def test_rendered_anchors_stay_equal_while_scrolling_and_resizing(app, tmp_path):
    widget, _, _ = make_widget(tmp_path)
    canvas = widget.canvas
    for days in (-1.37, -2.5, -10, 3.25):
        canvas.pan(days)
        widget.grab()
        assert canvas.painted["headings"] == canvas.painted["dots"]
        assert_equal_spacing(canvas.painted["headings"], canvas.viewport.pixels_per_day)
    widget.resize(900, 420)
    widget.grab()
    assert_equal_spacing(canvas.painted["headings"], canvas.viewport.pixels_per_day)


@pytest.mark.parametrize("now", [datetime(2026, 11, 1, 6, 30, tzinfo=CENTRAL), datetime(2026, 3, 8, 6, 30, tzinfo=CENTRAL)])
def test_rendered_widget_equal_spacing_on_dst_days(app, tmp_path, now):
    widget, _, _ = make_widget(tmp_path, now=now.astimezone(timezone.utc))
    canvas = widget.canvas
    for history in (True, False):
        widget.controller.set_history_enabled(history)
        widget.tick()
        for days in (0, -4, -7):
            canvas.follow_today()
            canvas.pan(days)
            widget.grab()
            assert_equal_spacing(canvas.painted["headings"], canvas.viewport.pixels_per_day)
            assert canvas.painted["headings"] == canvas.painted["dots"]


def test_history_toggle_keeps_future_anchors(app, tmp_path):
    widget, controller, _ = make_widget(tmp_path)
    canvas = widget.canvas
    on = dict(canvas.painted["groups"])
    on_headings = dict(canvas.painted["headings"])
    renderer = type(canvas)
    widget.history_toggle.click()
    assert not controller.settings.HistoryEnabled
    widget.grab()
    assert type(widget.canvas) is renderer and widget.canvas is canvas  # Same renderer for both modes.
    off = canvas.painted["groups"]
    assert canvas.markers == []
    today = canvas.viewport.today
    for day in on:
        if day >= today and day in off:
            assert off[day] == pytest.approx(on[day], abs=1e-6)
    shared = [d for d in on_headings if d >= today and d in canvas.painted["headings"]]
    assert shared
    for day in shared:
        assert canvas.painted["headings"][day] == pytest.approx(on_headings[day], abs=1e-6)
    widget.history_toggle.click()
    widget.grab()
    assert controller.settings.HistoryEnabled
    for day, x in on.items():
        assert canvas.painted["groups"][day] == pytest.approx(x, abs=1e-6)


def test_wheel_pans_and_is_always_consumed(app, tmp_path):
    widget, _, _ = make_widget(tmp_path)
    canvas = widget.canvas
    before = canvas.viewport.scroll_offset
    event = QWheelEvent(QPointF(300, 200), QPointF(300, 200), QPoint(0, 0), QPoint(0, 120), Qt.MouseButton.NoButton,
                        Qt.KeyboardModifier.NoModifier, Qt.ScrollPhase.NoScrollPhase, False)
    QApplication.sendEvent(canvas, event)
    assert event.isAccepted()
    assert canvas.viewport.scroll_offset == pytest.approx(before - canvas.viewport.pixels_per_day / 2)
    canvas.viewport.scroll_to(0)
    event = QWheelEvent(QPointF(300, 200), QPointF(300, 200), QPoint(0, 0), QPoint(0, 120), Qt.MouseButton.NoButton,
                        Qt.KeyboardModifier.NoModifier, Qt.ScrollPhase.NoScrollPhase, False)
    QApplication.sendEvent(canvas, event)
    assert event.isAccepted()  # Even at the boundary the page must not scroll instead.


def test_navigator_drag_is_stable_and_outside_click_recenters(app, tmp_path):
    widget, _, _ = make_widget(tmp_path)
    canvas, track = widget.canvas, widget.navigator.track
    canvas.viewport.scroll_to(canvas.viewport.max_scroll / 2)
    left, width = canvas.viewport.overview(track)
    before = canvas.viewport.scroll_offset
    canvas.begin_navigation(left + width / 2, track)
    canvas.drag_navigation(left + width / 2, track)
    assert canvas.viewport.scroll_offset == pytest.approx(before)
    canvas.drag_navigation(left + width / 2 + 10, track)
    assert canvas.viewport.scroll_offset == pytest.approx(before + 10 / track * canvas.viewport.content_width)
    canvas.end_navigation()
    canvas.begin_navigation(1, track)
    assert canvas.viewport.scroll_offset == pytest.approx(0)


def test_historical_markers_use_actual_timestamps(app, tmp_path):
    widget, _, _ = make_widget(tmp_path)
    canvas = widget.canvas
    canvas.viewport.scroll_to(0)
    canvas.pan(0)
    widget.grab()
    events = [e for s in canvas.visible for e in s.Events if e.At <= canvas.now]
    assert events
    for rect, _, _, _ in canvas.markers:
        assert any(rect.center().x() == pytest.approx(canvas.plot_x(e.At)) for e in events)
    for s in canvas.visible:
        for p in s.Points[:20]:
            assert canvas.content_x(p.At) == pytest.approx(canvas.viewport.position_at(p.At), abs=1e-6)


def test_keyboard_readout_and_escape(app, tmp_path):
    widget, _, _ = make_widget(tmp_path)
    canvas = widget.canvas
    canvas.viewport.scroll_to(0)
    canvas.pan(0)
    text = canvas.move_cursor(1)
    assert text and "% Used" in text
    canvas._show_readout(text, pinned=True)
    assert canvas.readout is not None
    canvas.clear_hover()
    assert canvas.readout is None and canvas.cursor is None


def test_tile_click_opens_event(app, tmp_path):
    widget, _, opened = make_widget(tmp_path)
    canvas = widget.canvas
    rect, event, _, _ = canvas.tiles[0]
    from PyQt6.QtTest import QTest
    QTest.mouseClick(canvas, Qt.MouseButton.LeftButton, Qt.KeyboardModifier.NoModifier, rect.center().toPoint())
    assert opened == [event]


def test_selector_hides_account_from_timeline(app, tmp_path):
    widget, controller, _ = make_widget(tmp_path)
    before = widget.refresh_label.text()
    widget._select("demo-personal", False)
    assert not controller.settings.timeline_includes("demo-personal")
    assert all(e.AccountId != "demo-personal" for e in widget.canvas.future)
    assert widget.refresh_label.text() != before


def test_place_labels_keeps_order_inside_bounds():
    assert place_labels([10, 12, 300], 20, 200) == [20, 45, 200]
    positions = place_labels([100, 100, 100], 0, 30)
    assert positions == sorted(positions) and positions[0] >= 0 and positions[-1] <= 30
