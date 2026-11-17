"""Global and per-account Settings panels (port of Views/SettingsPage.cs and its controls).

Every change saves automatically. Saves go through the controller, which persists before adopting,
so a failed write leaves the previous settings in force and keeps a status message on screen. A panel
with a pending status stays open on Close so the problem cannot be dismissed silently."""
from __future__ import annotations

import uuid
from typing import Callable, Optional, Sequence

from PyQt6.QtCore import QPointF, QRect, QRectF, QSize, Qt, QTimer, pyqtSignal
from PyQt6.QtGui import QColor, QIntValidator, QPainter, QPen
from PyQt6.QtWidgets import (QButtonGroup, QColorDialog, QComboBox, QGridLayout, QHBoxLayout, QLineEdit, QPushButton,
                             QRadioButton, QSizePolicy, QSlider, QVBoxLayout, QWidget)

from ..core.labels import format_reset, plan_label, system_zone_name, zone_choices
from ..core.model import ProviderAccount, replace
from ..core.settings import RadarSettings, ReminderSeverity, TimedReminder
from . import theme as T
from .panels import Dialog, Panel, confirm, message, section
from .presentation import QuotaRow, account_color
from .widgets import ActionButton, HelpIcon, SwitchRow, ToggleSwitch, label, set_label_color

SAVE_DELAY_MS = 350
RETENTION_OPTIONS = (7, 30, 60, 90, 180, 365)
LINE_PALETTE = ("#B99AF8", "#CDEB82", "#58A6FF", "#D587A5", "#55C9BD", "#F2C94C", "#F05B69")
SOUND_CUES = (("Alarm", "alert"), ("Snooze", "snooze"), ("Dismiss", "dismiss"), ("Show Info", "info"))

HELP = {
    "Popup Notifications": "Show popup alerts when an enabled alert rule is triggered",
    "Alert on Icon": "Show alert status on the app icon, even when popup notifications are off",
    "Notification Sounds": "Play the selected sounds for alerts and notification actions",
    "Monitor This Account": "Pause automatic usage checks and alerts when off. Your saved connection stays in place.",
    "Capacity Exhausted": "Alert when a usage limit reaches 0% remaining. Enable the alarm on that limit's dashboard row "
                          "to receive this alert.",
    "Capacity Restored": "Alert when a previously exhausted limit has capacity again and the provider confirms usage is "
                         "allowed. Enable the alarm on that limit's dashboard row to receive this alert.",
    "Reset Expiry Reminders": "Remind you before an unused earned reset expires, using Earned Reset Expiration Alerts in "
                              "global Settings. Expiration does not refresh your quota. At least one dashboard row alarm "
                              "for this account must be on.",
    "Keep Monitoring When Window Closes": "Keep checking usage from the menu bar or system tray after closing the window. "
                                          "Use Exit from the tray menu to stop Scrooge.",
    "Launch at Sign-In": "Start Scrooge automatically when you sign in to this computer",
    "Notifications": "Popup notifications show triggered alerts. Alert on Icon keeps alert status visible on the app "
                     "icon, even when popups are off.",
    "Sounds": "Choose a sound style and volume for alerts and notification actions. Preview buttons play each sound at "
              "the selected volume.",
    "App Behavior": "Keep monitoring after closing the window by leaving Scrooge in the menu bar or system tray. Use Exit "
                    "in the tray menu to stop it. Launch at Sign-In starts Scrooge automatically.",
    "Account": "Name and color identify this account. Polling controls how often usage is checked. Turning monitoring "
               "off pauses checks and alerts while keeping the saved connection.",
    "Connection": "Sign in again to renew the connection while keeping settings. Removing the connection only removes "
                  "it from Scrooge; your provider account is unaffected.",
    "Time Zone": "Use this time zone for displayed dates, reset times, and quiet hours",
    "Quiet Hours": "Keep monitoring while notification delivery is quiet during the selected hours. Drag the start and "
                   "end handles or use arrow keys in one-hour steps.",
    "Default Alert Levels": "Alerts use capacity remaining, not used. Critical must be below Warning; Info must be at or "
                            "above Warning. Adjust individual limits on their dashboard bars.",
    "Earned Reset Expiration Alerts": "Reminders before an earned reset expires. Expiration does not refresh your quota. "
                                      "Each row sets severity and lead time in days or hours. Reminders sort from "
                                      "longest to shortest lead time.",
    "Quota Refresh Alerts": "Reminders before a scheduled quota refresh, separate from earned reset expirations. Each "
                            "row sets severity and lead time in days or hours. Reminders sort from longest to shortest "
                            "lead time.",
    "Available Resets": "Unused earned resets reported by this provider, with their expiration times when available",
    "Polling Interval": "How often Scrooge automatically checks this account for updated usage, from 1 to 30 minutes. "
                        "Shorter intervals show changes sooner; longer intervals make fewer requests. Changes save "
                        "automatically.",
    "Add Custom Line Color": "Add Custom Line Color",
}

SAVE_FAILED = "Settings could not be saved. Check available space and local folder access."
SEVERITY_NAMES = ("Info", "Warning", "Critical")


def _version() -> str:
    import token_scrooge
    return getattr(token_scrooge, "__version__", "0.1.0")


def _hour(hour: int) -> str:
    return f"{(hour % 12) or 12} {'AM' if hour < 12 else 'PM'}"


def _combo_color(combo: QComboBox, color: str) -> None:
    combo.setStyleSheet(f"QComboBox {{ color: {color}; }}")


def _valid_hex(value: Optional[str]) -> bool:
    return bool(value) and len(value) == 7 and value[0] == "#" and all(c in "0123456789abcdefABCDEF" for c in value[1:])


# ---------------------------------------------------------------------------------------------- controls
class PercentSlider(QWidget):
    """'Info Alert @ 50% Left' label over a colored 0-100 slider in whole-percent steps."""
    changed = pyqtSignal()
    released = pyqtSignal()

    def __init__(self, title: str, value: float, color: str, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.title = title
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(2)
        self.caption = label("", 14, color)
        self.slider = QSlider(Qt.Orientation.Horizontal)
        self.slider.setRange(0, 100)
        self.slider.setValue(int(round(max(0.0, min(100.0, float(value))))))
        self.slider.setAccessibleName(f"{title}: percent remaining")
        self.slider.setStyleSheet(
            f"QSlider::sub-page:horizontal {{ background: {color}; border-radius: 2px; }}"
            f"QSlider::handle:horizontal {{ background: {color}; width: 18px; height: 18px; margin: -7px 0; border-radius: 9px; }}")
        self.slider.valueChanged.connect(self._moved)
        self.slider.sliderReleased.connect(self.released)
        layout.addWidget(self.caption)
        layout.addWidget(self.slider)
        self._update()

    @property
    def value(self) -> int:
        return self.slider.value()

    def set_value(self, value: float) -> None:
        self.slider.blockSignals(True)
        self.slider.setValue(int(round(value)))
        self.slider.blockSignals(False)
        self._update()

    def _moved(self, _value: int) -> None:
        self._update()
        self.changed.emit()

    def _update(self) -> None:
        self.caption.setText(f"{self.title} @ {self.slider.value()}% Left")


class QuietHoursRange(QWidget):
    """Noon-to-noon track so the usual overnight interval stays continuous. Drag either handle, or focus
    the widget and use Left/Right (Up/Down) in one-hour steps; Tab-free switching uses Space."""
    changed = pyqtSignal()
    INSET = 32
    TIME_SIZE = 16

    def __init__(self, start_hour: int, end_hour: int, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.start_hour = max(0, min(23, int(start_hour)))
        self.end_hour = max(0, min(23, int(end_hour)))
        self._moving_start = True
        self._dragging = False
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Fixed)
        self._height = 0
        self._update_accessibility()

    # geometry -------------------------------------------------------------------------------
    @staticmethod
    def position(hour: int, end: bool = False) -> float:
        return 1.0 if end and hour == 12 else ((hour + 12) % 24) / 24

    def _span(self) -> tuple[float, float]:
        left = float(self.INSET)
        right = max(left, self.width() - self.INSET)
        return left, right - left

    def handle_x(self, start: bool) -> float:
        left, span = self._span()
        return left + self.position(self.start_hour if start else self.end_hour, not start) * span

    def _close(self) -> bool:
        return abs(self.handle_x(True) - self.handle_x(False)) < 106

    def _track_y(self) -> float:
        return 66.0 if self._close() else 44.0

    def summary(self) -> str:
        duration = (self.end_hour - self.start_hour + 24) % 24
        if duration == 0:
            return "Choose different start and end times to set a quiet period."
        overnight = " · Overnight" if self.end_hour < self.start_hour else ""
        return f"{_hour(self.start_hour)} – {_hour(self.end_hour)} · {duration} hours{overnight}"

    def sizeHint(self) -> QSize:
        return QSize(480, self._wanted_height())

    def _wanted_height(self) -> int:
        return int(self._track_y()) + 50  # Track, then the tick labels below it.

    def _sync_height(self) -> None:
        wanted = self._wanted_height()
        if wanted != self._height:
            self._height = wanted
            self.setFixedHeight(wanted)

    def resizeEvent(self, e):
        self._sync_height()

    # interaction ----------------------------------------------------------------------------
    def set_hours(self, start: int, end: int) -> None:
        self.start_hour, self.end_hour = start % 24, end % 24
        self._update_accessibility()
        self.update()

    def _move(self, x: float) -> None:
        left, span = self._span()
        offset = round(max(0.0, min(1.0, (x - left) / max(1.0, span))) * 24)
        hour = (offset + 12) % 24
        if self._moving_start:
            self.start_hour = hour
        else:
            self.end_hour = hour
        self._update_accessibility()
        self.update()

    def mousePressEvent(self, e):
        if not self.isEnabled() or e.button() != Qt.MouseButton.LeftButton:
            return
        x = e.position().x()
        a, b = abs(x - self.handle_x(True)), abs(x - self.handle_x(False))
        self._moving_start = (not self._moving_start) if a == b else a < b
        self._dragging = True
        self._move(x)

    def mouseMoveEvent(self, e):
        if self._dragging and self.isEnabled():
            self._move(e.position().x())

    def mouseReleaseEvent(self, e):
        if self._dragging:
            self._dragging = False
            self.changed.emit()

    def keyPressEvent(self, e):
        if not self.isEnabled():
            return super().keyPressEvent(e)
        key = e.key()
        if key == Qt.Key.Key_Space:
            self._moving_start = not self._moving_start
            self._update_accessibility()
            self.update()
            return
        step = {Qt.Key.Key_Left: -1, Qt.Key.Key_Down: -1, Qt.Key.Key_Right: 1, Qt.Key.Key_Up: 1}.get(key, 0)
        if not step:
            return super().keyPressEvent(e)
        if self._moving_start:
            self.start_hour = (self.start_hour + step) % 24
        else:
            self.end_hour = (self.end_hour + step) % 24
        self._update_accessibility()
        self.update()
        self.changed.emit()

    def _update_accessibility(self) -> None:
        self._sync_height()
        which = "Start" if self._moving_start else "End"
        self.setAccessibleName("Quiet Hours")
        self.setAccessibleDescription(
            f"Start {_hour(self.start_hour)}, end {_hour(self.end_hour)}. Arrow keys adjust the {which.lower()} time; "
            "Space switches between start and end.")

    # painting -------------------------------------------------------------------------------
    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        if not self.isEnabled():
            p.setOpacity(0.5)
        left, span = self._span()
        right = left + span
        a, b = self.handle_x(True), self.handle_x(False)
        close = self._close()
        y = self._track_y()
        pen = QPen(QColor(T.SLIDER_TRACK), 6, cap=Qt.PenCapStyle.RoundCap)
        p.setPen(pen)
        p.drawLine(QPointF(left, y), QPointF(right, y))
        pen.setColor(QColor(T.SLIDER_FILL))
        p.setPen(pen)
        if self.start_hour != self.end_hour:
            if a < b:
                p.drawLine(QPointF(a, y), QPointF(b, y))
            else:
                p.drawLine(QPointF(a, y), QPointF(right, y))
                p.drawLine(QPointF(left, y), QPointF(b, y))
        for x, is_start in ((a, True), (b, False)):
            p.setBrush(QColor(T.HIGHLIGHT))
            active = self.hasFocus() and is_start == self._moving_start
            p.setPen(QPen(QColor("#FFFFFF" if active else "#7160B4"), 3))
            p.drawEllipse(QPointF(x, y), 12, 12)
        # Time labels above each handle; the end label drops a row when the handles are close.
        p.setFont(T.font(self.TIME_SIZE))
        for x, hour, top in ((a, self.start_hour, 0), (b, self.end_hour, 22 if close else 0)):
            box = QRectF(max(0.0, min(self.width() - 106.0, x - 53)), top, 106, 28)
            p.setPen(QColor(T.TEXT))
            p.drawText(box, Qt.AlignmentFlag.AlignCenter, _hour(hour))
        p.setPen(QColor(T.HIGHLIGHT))
        for i, tick in enumerate(("12 PM", "6 PM", "12 AM", "6 AM", "12 PM")):
            p.drawText(QRectF(left + span * i / 4 - 32, y + 22, 64, 24), Qt.AlignmentFlag.AlignCenter, tick)


class MinuteSlider(QWidget):
    """1-30 minute slider whose ticks and snapped thumb share one scale."""
    valueChanged = pyqtSignal(int)
    released = pyqtSignal()
    INSET = 14

    def __init__(self, value: int, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._value = max(1, min(30, int(value)))
        self._dragging = False
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        self.setFixedHeight(44)
        self.setMinimumWidth(200)
        self.setAccessibleName("Polling Interval in Minutes, 1 to 30")

    def value(self) -> int:
        return self._value

    def setValue(self, value: int) -> None:
        value = max(1, min(30, int(value)))
        if value != self._value:
            self._value = value
            self.update()
            self.valueChanged.emit(value)

    def anchor(self, minute: int) -> float:
        width = max(1.0, self.width() - self.INSET * 2)
        return self.INSET + (minute - 1) / 29 * width

    def minute_at(self, x: float) -> int:
        width = max(1.0, self.width() - self.INSET * 2)
        return max(1, min(30, round((x - self.INSET) / width * 29) + 1))

    def mousePressEvent(self, e):
        if e.button() == Qt.MouseButton.LeftButton:
            self._dragging = True
            self.setValue(self.minute_at(e.position().x()))

    def mouseMoveEvent(self, e):
        if self._dragging:
            self.setValue(self.minute_at(e.position().x()))

    def mouseReleaseEvent(self, e):
        if self._dragging:
            self._dragging = False
            self.released.emit()

    def keyPressEvent(self, e):
        target = {Qt.Key.Key_Left: self._value - 1, Qt.Key.Key_Down: self._value - 1, Qt.Key.Key_Right: self._value + 1,
                  Qt.Key.Key_Up: self._value + 1, Qt.Key.Key_Home: 1, Qt.Key.Key_End: 30}.get(e.key())
        if target is None:
            return super().keyPressEvent(e)
        self.setValue(target)
        self.released.emit()

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        mid = self.height() / 2
        left, right = self.anchor(1), self.anchor(30)
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(QColor(T.SLIDER_TRACK))
        p.drawRoundedRect(QRectF(left, mid - 2, right - left, 4), 2, 2)
        x = self.anchor(self._value)
        p.setBrush(QColor(T.SLIDER_FILL))
        p.drawRoundedRect(QRectF(left, mid - 2, x - left, 4), 2, 2)
        p.setPen(QPen(QColor(T.SLIDER_FILL), 1))
        for minute in range(1, 31):
            tx = round(self.anchor(minute)) + 0.5
            p.drawLine(QPointF(tx, mid + 6), QPointF(tx, mid + 12 if minute % 5 == 0 else mid + 9))
        p.setPen(QPen(QColor(T.SLIDER_TRACK), 5))
        p.setBrush(QColor(T.HIGHLIGHT))
        p.drawEllipse(QPointF(x, mid), 10, 10)
        if self.hasFocus():
            p.setPen(QPen(QColor(T.ACCENT), 1.5))
            p.setBrush(Qt.BrushStyle.NoBrush)
            p.drawEllipse(QPointF(x, mid), 14, 14)


class ReminderEditor(QWidget):
    """Rows of severity / amount / Days-or-Hours, sorted from the longest lead time to the shortest."""
    changed = pyqtSignal()

    def __init__(self, initial: Sequence[TimedReminder], parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._reminders: list[TimedReminder] = sorted(initial, key=lambda r: -r.hours)
        self._rows: dict[str, QWidget] = {}
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(8)
        self._rows_layout = QVBoxLayout()
        self._rows_layout.setSpacing(6)
        layout.addLayout(self._rows_layout)
        self.error = label("", 12, T.CRITICAL)
        self.error.hide()
        layout.addWidget(self.error)
        add = ActionButton("Add Reminder", "plus")
        add.clicked.connect(self.add_reminder)
        add_row = QHBoxLayout()
        add_row.addWidget(add)
        add_row.addStretch(1)
        layout.addLayout(add_row)
        for item in self._reminders:
            self._add_row(item)

    @property
    def value(self) -> tuple[TimedReminder, ...]:
        return tuple(sorted(self._reminders, key=lambda r: -r.hours))

    def add_reminder(self) -> None:
        item = TimedReminder(uuid.uuid4().hex, 1, "Days", ReminderSeverity.Info)
        self._reminders.append(item)
        self._add_row(item)
        self._sort_rows()
        self.changed.emit()

    def _index(self, reminder_id: str) -> int:
        return next((i for i, r in enumerate(self._reminders) if r.Id == reminder_id), -1)

    def _add_row(self, item: TimedReminder) -> None:
        row = QWidget()
        grid = QHBoxLayout(row)
        grid.setContentsMargins(0, 0, 0, 0)
        grid.setSpacing(8)
        severity = QComboBox()
        severity.addItems(SEVERITY_NAMES)
        severity.setCurrentIndex(int(item.Severity))
        severity.setAccessibleName("Reminder Severity")
        _combo_color(severity, T.SEVERITY_COLORS[int(item.Severity)])
        amount = QLineEdit(str(item.Amount))
        amount.setValidator(QIntValidator(1, 9999, amount))
        amount.setFixedWidth(80)
        amount.setAccessibleName("Time Before Event")
        unit = QComboBox()
        unit.addItems(("Days", "Hours"))
        unit.setCurrentIndex(0 if item.Unit == "Days" else 1)
        unit.setAccessibleName("Days or Hours")
        remove = ActionButton("Remove Reminder", "trash", icon_only=True)
        remove.setToolTip("Remove This Reminder from the Schedule")
        remove.setFixedWidth(44)
        grid.addWidget(severity, 1)
        grid.addWidget(amount)
        grid.addWidget(unit, 1)
        grid.addWidget(remove)
        self._rows[item.Id] = row
        self._rows_layout.addWidget(row)

        def save(resort: bool) -> None:
            text = amount.text().strip()
            if not text.isdigit() or int(text) <= 0:
                self.error.setText("Enter a Positive Whole Number")
                self.error.show()
                return
            self.error.hide()
            index = self._index(item.Id)
            if index < 0:
                return
            level = max(0, severity.currentIndex())
            _combo_color(severity, T.SEVERITY_COLORS[level])
            updated = replace(self._reminders[index], Amount=int(text), Unit="Days" if unit.currentIndex() == 0 else "Hours",
                              Severity=ReminderSeverity(level))
            if updated == self._reminders[index]:
                if resort:
                    self._sort_rows()
                return
            self._reminders[index] = updated
            if resort:
                self._sort_rows()
            self.changed.emit()

        def drop() -> None:
            self._reminders = [r for r in self._reminders if r.Id != item.Id]
            self._rows.pop(item.Id, None)
            row.hide()
            row.deleteLater()
            self.error.hide()
            self._sort_rows()
            self.changed.emit()

        # Rows re-sort when a field is committed, never mid-typing, so the focused row does not jump.
        amount.textEdited.connect(lambda _t: save(False))
        amount.editingFinished.connect(lambda: save(True))
        severity.currentIndexChanged.connect(lambda _i: save(True))
        unit.currentIndexChanged.connect(lambda _i: save(True))
        remove.clicked.connect(drop)

    def _sort_rows(self) -> None:
        self._reminders.sort(key=lambda r: -r.hours)
        for reminder in self._reminders:
            widget = self._rows.get(reminder.Id)
            if widget is not None:
                self._rows_layout.removeWidget(widget)
                self._rows_layout.addWidget(widget)


class ColorSwatch(QPushButton):
    def __init__(self, hex_color: str, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.hex = hex_color.upper()
        self.selected = False
        self.setFixedSize(32, 32)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setAccessibleName(f"Line Color {self.hex}")
        self.setToolTip(self.hex)

    def set_selected(self, on: bool) -> None:
        self.selected = on
        self.setAccessibleDescription("Selected" if on else "")
        self.update()

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        rect = QRectF(self.rect()).adjusted(1, 1, -1, -1)
        p.setPen(QPen(QColor("#FFFFFF"), 2) if self.selected else (QPen(QColor(T.ACCENT), 1.5) if self.hasFocus() else Qt.PenStyle.NoPen))
        p.setBrush(QColor(self.hex))
        p.drawRoundedRect(rect, 9, 9)
        if self.selected:
            T.draw_icon(p, "check", rect.adjusted(7, 7, -7, -7), T.CANVAS, 2.2)


class AddColorButton(QPushButton):
    def __init__(self, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.setFixedSize(32, 32)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setAccessibleName("Add Custom Line Color")
        self.setToolTip("Add Custom Line Color")

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        hover = self.underMouse() or self.hasFocus()
        p.setPen(QPen(QColor(T.ACCENT if hover else T.POPUP_BORDER), 1))
        p.setBrush(QColor(T.INTERACTION if hover else T.ACTION))
        p.drawRoundedRect(QRectF(self.rect()).adjusted(0.5, 0.5, -0.5, -0.5), 9, 9)
        T.draw_icon(p, "plus", QRectF(8, 8, 16, 16), "#FFFFFF" if hover else T.ACCENT)


class FlowRow(QWidget):
    """Wrapping row of fixed-size children (the swatch list)."""

    def __init__(self, spacing: int = 8, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._items: list[QWidget] = []
        self._spacing = spacing
        self.setSizePolicy(QSizePolicy.Policy.Expanding, QSizePolicy.Policy.Preferred)

    def items(self) -> list[QWidget]:
        return list(self._items)

    def insert(self, index: int, widget: QWidget) -> None:
        widget.setParent(self)
        self._items.insert(index, widget)
        widget.show()
        self._relayout()

    def append(self, widget: QWidget) -> None:
        self.insert(len(self._items), widget)

    def hasHeightForWidth(self) -> bool:
        return True

    def heightForWidth(self, width: int) -> int:
        return self._positions(width)[1]

    def sizeHint(self) -> QSize:
        return QSize(320, self.heightForWidth(max(self.width(), 320)))

    def minimumSizeHint(self) -> QSize:
        return QSize(40, self.heightForWidth(max(self.width(), 40)))

    def _positions(self, width: int) -> tuple[list[QRect], int]:
        x = y = row_h = 0
        rects = []
        for w in self._items:
            size = w.size()
            if x and x + size.width() > width:
                x, y = 0, y + row_h + self._spacing
                row_h = 0
            rects.append(QRect(x, y, size.width(), size.height()))
            x += size.width() + self._spacing
            row_h = max(row_h, size.height())
        return rects, y + row_h

    def _relayout(self) -> None:
        rects, height = self._positions(self.width())
        for w, r in zip(self._items, rects):
            w.setGeometry(r)
        if self.minimumHeight() != height:
            self.setMinimumHeight(height)
            self.updateGeometry()

    def resizeEvent(self, e):
        self._relayout()


# ---------------------------------------------------------------------------------------------- base
class _SettingsPanel(Panel):
    def __init__(self, controller, title: str):
        super().__init__(title)
        self.controller = controller
        self._sections: dict[str, QWidget] = {}
        self._busy_saving = False
        self.status = label("", 13, T.EXPIRY, wrap=True, align=Qt.AlignmentFlag.AlignRight)
        self.status.setContentsMargins(24, 6, 24, 10)
        self.status.hide()
        self.layout().addWidget(self.status)

    @property
    def demo(self) -> bool:
        return self.controller.state.is_demo

    def set_status(self, text: str) -> None:
        self.status.setText(text)
        self.status.setVisible(bool(text))

    def status_text(self) -> str:
        return self.status.text()

    def card(self, title: str, description: Optional[str] = None,
             header_widget: Optional[QWidget] = None) -> QVBoxLayout:
        card, layout = section(title, HELP.get(title), header_widget)
        card.setAccessibleName(title)
        if description:
            layout.addWidget(label(description, 13, T.TEXT_TERTIARY, wrap=True))
        self._sections[title] = card
        self.add(card)
        return layout

    def section_widget(self, title: str) -> Optional[QWidget]:
        return self._sections.get(title)

    def toggle(self, layout: QVBoxLayout, title: str, value: bool, help_text: bool = False,
               display: Optional[str] = None) -> SwitchRow:
        row = SwitchRow(display or title, value, HELP.get(title) if help_text else None)
        row.setMinimumHeight(36)
        layout.addWidget(row)
        return row

    def scroll_to(self, title: str) -> None:
        target = self._sections.get(title)
        if target is None:
            return
        self.scroll.verticalScrollBar().setValue(max(0, target.mapTo(self.scroll.widget(), target.rect().topLeft()).y() - 8))

    def account_error(self) -> None:
        if self.host is not None:
            message(self.host, "Account Update Unavailable",
                    "Scrooge could not save this change. Check local folder access and retry.")
        else:
            self.set_status("Account Update Unavailable")


# ---------------------------------------------------------------------------------------------- global
class GlobalSettingsPanel(_SettingsPanel):
    """App-wide settings. `open_alert_history()` and `test_notification()` are host actions;
    `launch_at_login(enabled)` applies the OS autostart change and raises on failure;
    `focus_section` (a card title such as "Earned Reset Expiration Alerts") scrolls there on open."""

    def __init__(self, controller, open_alert_history: Callable[[], None], test_notification: Callable[[], None],
                 launch_at_login: Optional[Callable[[bool], None]] = None, focus_section: Optional[str] = None):
        super().__init__(controller, "Settings")
        self._open_history = open_alert_history
        self._test = test_notification
        self._launch_at_login = launch_at_login
        self._focus = focus_section
        self._focused = False
        self._timer = QTimer(self)
        self._timer.setSingleShot(True)
        self._timer.setInterval(SAVE_DELAY_MS)
        self._timer.timeout.connect(self.autosave)
        self._build(controller.settings)

    # construction ---------------------------------------------------------------------------
    def _build(self, s: RadarSettings) -> None:
        if self.demo:
            self.add(label("Demo changes stay in this preview. Notification test uses your desktop notifications.",
                           14, T.ACCENT, wrap=True))

        layout = self.card("App Updates")
        layout.addWidget(label(f"Version {_version()}", 16, T.TEXT))
        layout.addWidget(label("Automatic update checks are not available in this version yet. Check the project "
                               "releases on GitHub for newer versions.", 13, T.TEXT_DETAIL, wrap=True))

        layout = self.card("Time Zone")
        self.time_zone = QComboBox()
        self.time_zone.setAccessibleName("Time Zone for dates and quiet hours")
        self.time_zone.setMaxVisibleItems(18)
        self.time_zone.addItem(f"System Time Zone ({system_zone_name()})", "")
        zones = zone_choices()
        if s.TimeZoneId and s.TimeZoneId not in zones:
            zones = sorted([*zones, s.TimeZoneId])
        for zone in zones:
            self.time_zone.addItem(zone.replace("_", " "), zone)
        self.time_zone.setCurrentIndex(max(0, self.time_zone.findData(s.TimeZoneId or "")))
        self.time_zone.currentIndexChanged.connect(lambda _i: self.autosave())
        layout.addWidget(self.time_zone)

        layout = self.card("History and Alerts", "Choose how long to keep recorded usage and alerts. Shortening this "
                                                 "period removes older records; increasing it cannot restore deleted records.")
        self.retention_options = sorted({*RETENTION_OPTIONS, s.retention_days})
        self.retention = QComboBox()
        self.retention.setAccessibleName("History and Alert Retention")
        for days in self.retention_options:
            self.retention.addItem(f"{days} Days", days)
        self.retention.setCurrentIndex(self.retention_options.index(s.retention_days))
        self.retention.currentIndexChanged.connect(lambda _i: self.autosave())
        layout.addWidget(self.retention)

        layout = self.card("Notifications", "Choose how Scrooge gets your attention.")
        self.notifications = self.toggle(layout, "Popup Notifications", s.Notifications)
        self.icon_alerts = self.toggle(layout, "Alert on Icon", s.AlertOnIcon)

        layout = self.card("Sounds")
        self.sound = self.toggle(layout, "Notification Sounds", s.Sound)
        volume_row = QHBoxLayout()
        volume_row.setSpacing(12)
        self.volume_label = label("", 14, T.TEXT_DETAIL)
        self.volume_label.setMinimumWidth(112)
        self.volume = QSlider(Qt.Orientation.Horizontal)
        self.volume.setRange(0, 100)
        volume = s.SoundVolume if s.SoundVolume == s.SoundVolume and abs(s.SoundVolume) != float("inf") else 65
        self.volume.setValue(int(round(max(0, min(100, volume)))))
        self.volume.setAccessibleName("Notification Volume")
        self.volume.valueChanged.connect(self._volume_moved)
        self.volume.sliderReleased.connect(self.autosave)
        volume_row.addWidget(self.volume_label)
        volume_row.addWidget(self.volume, 1)
        layout.addLayout(volume_row)
        self._volume_moved(self.volume.value(), save=False)
        self.duck_sounds = QRadioButton("Duck Sounds")
        self.classic_chimes = QRadioButton("Classic Chimes")
        group = QButtonGroup(self)
        for button in (self.duck_sounds, self.classic_chimes):
            button.setFont(T.font(14))
            group.addButton(button)
            layout.addWidget(button)
        (self.classic_chimes if s.SoundTheme == "Classic" else self.duck_sounds).setChecked(True)
        group.buttonToggled.connect(lambda _b, checked: checked and self.autosave())
        previews = QGridLayout()
        previews.setHorizontalSpacing(8)
        previews.setVerticalSpacing(8)
        self.preview_status = label("", 12, T.EXPIRY, wrap=True)
        for i, (name, cue) in enumerate(SOUND_CUES):
            button = ActionButton(f"Preview {name}")
            button.clicked.connect(lambda _=False, c=cue: self._preview(c))
            previews.addWidget(button, i // 2, i % 2)
        layout.addLayout(previews)
        self.preview_status.hide()
        layout.addWidget(self.preview_status)

        self.quiet = ToggleSwitch(s.QuietHours)
        self.quiet.setAccessibleName("Enable Quiet Hours")
        layout = self.card("Quiet Hours", "A little peace, right on schedule.", self.quiet)
        self.quiet_range = QuietHoursRange(s.QuietStartHour, s.QuietEndHour)
        self.quiet_summary = label(self.quiet_range.summary(), 16, T.HIGHLIGHT)
        self.quiet_range.changed.connect(self._quiet_changed)
        layout.addWidget(self.quiet_range)
        layout.addWidget(self.quiet_summary)
        self._quiet_enabled(s.QuietHours)

        layout = self.card("Default Alert Levels")
        self.info = PercentSlider("Info Alert", max(s.InfoThreshold, s.LowThreshold), T.INFO)
        self.low = PercentSlider("Warning Alert", s.LowThreshold, T.WARNING)
        self.critical = PercentSlider("Critical Alert", s.CriticalThreshold, T.CRITICAL)
        for slider in (self.info, self.low, self.critical):
            slider.changed.connect(self.schedule)
            slider.released.connect(self.autosave)
            layout.addWidget(slider)

        layout = self.card("Earned Reset Expiration Alerts")
        self.reset_reminders = ReminderEditor(s.effective_reset_reminders)
        self.reset_reminders.changed.connect(self.schedule)
        layout.addWidget(self.reset_reminders)

        layout = self.card("Quota Refresh Alerts")
        self.refresh_reminders = ReminderEditor(s.effective_refresh_reminders)
        self.refresh_reminders.changed.connect(self.schedule)
        layout.addWidget(self.refresh_reminders)

        layout = self.card("App Behavior")
        self.close_to_tray = self.toggle(layout, "Keep Monitoring When Window Closes", s.CloseToTray)
        self.startup = self.toggle(layout, "Launch at Sign-In", s.LaunchAtSignIn)
        self.startup.setEnabled(not self.demo and self._launch_at_login is not None)
        actions = QHBoxLayout()
        actions.setSpacing(8)
        self.history_button = ActionButton("Alert History", "history")
        self.history_button.clicked.connect(self._open_history)
        self.test_button = ActionButton("Send Test Notification", "info")
        self.test_button.clicked.connect(self._send_test)
        actions.addWidget(self.history_button)
        actions.addWidget(self.test_button)
        actions.addStretch(1)
        layout.addLayout(actions)

        for row in (self.notifications, self.icon_alerts, self.sound, self.close_to_tray):
            row.changed.connect(lambda _v: self.autosave())
        self.quiet.toggled_by_user.connect(self._quiet_toggled)
        self.startup.changed.connect(self._startup_changed)
        self.layout_body.addStretch(1)

    def showEvent(self, e):
        super().showEvent(e)
        if self._focus and not self._focused:
            self._focused = True
            QTimer.singleShot(0, lambda: self.scroll_to(self._focus))

    # events ---------------------------------------------------------------------------------
    def _volume_moved(self, value: int, save: bool = True) -> None:
        self.volume_label.setText(f"Volume: {value}%")
        if save:
            self.schedule()

    def _quiet_enabled(self, on: bool) -> None:
        self.quiet_range.setEnabled(on)
        self.quiet_summary.setEnabled(on)
        set_label_color(self.quiet_summary, T.HIGHLIGHT if on else T.TEXT_TERTIARY)

    def _quiet_toggled(self, on: bool) -> None:
        self._quiet_enabled(on)
        self.autosave()

    def _quiet_changed(self) -> None:
        self.quiet_summary.setText(self.quiet_range.summary())
        self.autosave()

    def _preview(self, cue: str) -> None:
        candidate = replace(self.controller.settings, SoundTheme=self._theme(), SoundVolume=float(self.volume.value()))
        sounds = getattr(self.controller, "sounds", None)
        if sounds is None:
            self.preview_status.setText("Sound Playback Is Unavailable")
            self.preview_status.show()
            return
        try:
            result = sounds.play(candidate, cue, True)
        except Exception:  # noqa: BLE001 - playback problems are shown, never fatal
            result = "Sound Could Not Be Played"
        self.preview_status.setText(result if isinstance(result, str) else "")
        self.preview_status.setVisible(bool(self.preview_status.text()))

    def _send_test(self) -> None:
        self.test_button.set_busy("Sending…")
        try:
            self._test()
        except Exception:  # noqa: BLE001
            self.set_status("Test Notification Could Not Be Sent")
        finally:
            self.test_button.set_busy(None)

    def _theme(self) -> str:
        return "Classic" if self.classic_chimes.isChecked() else "Ducks"

    def _startup_changed(self, enabled: bool) -> None:
        if not self.autosave():
            return
        if self._launch_at_login is None or self.demo:
            return
        try:
            self._launch_at_login(enabled)
        except Exception:  # noqa: BLE001 - restore the saved preference to the real OS state
            self.startup.set_checked(not enabled)
            try:
                self.controller.save_settings(replace(self.controller.settings, LaunchAtSignIn=not enabled))
            except Exception:  # noqa: BLE001
                pass
            self.set_status("Launch at Sign-In Could Not Be Changed")

    # saving ---------------------------------------------------------------------------------
    def schedule(self) -> None:
        self._timer.start()

    def candidate(self) -> tuple[Optional[RadarSettings], str]:
        """Build settings from the controls on top of the latest saved settings, or return the problem."""
        start, end = self.quiet_range.start_hour, self.quiet_range.end_hour
        low, critical, info = float(self.low.value), float(self.critical.value), float(self.info.value)
        quiet = self.quiet.isChecked()
        if quiet and start == end:
            return None, "Choose different start and end times for quiet hours."
        if info < low:
            return None, "Info Must Be At or Above Warning"
        if critical >= low:
            return None, "Critical capacity must be lower than warning capacity."
        latest = self.controller.settings
        account_ids = {a.Id for a in self.controller.state.accounts}
        rules = tuple(r for r in latest.Rules or () if r.AccountId in account_ids) if latest.Rules is not None else None
        updated = replace(latest,
                          HistoryRetentionDays=int(self.retention.currentData()),
                          ResetExpiryReminders=self.reset_reminders.value,
                          QuotaRefreshReminders=self.refresh_reminders.value,
                          Notifications=self.notifications.is_checked(), AlertOnIcon=self.icon_alerts.is_checked(),
                          Sound=self.sound.is_checked(), QuietHours=quiet, QuietStartHour=start, QuietEndHour=end,
                          InfoThreshold=info, LowThreshold=low, CriticalThreshold=critical,
                          LaunchAtSignIn=self.startup.is_checked(), CloseToTray=self.close_to_tray.is_checked(),
                          Rules=rules, SoundTheme=self._theme(), SoundVolume=float(self.volume.value()),
                          TimeZoneId=self.time_zone.currentData() or "")
        if not updated.has_valid_thresholds:
            return None, "An account or limit rule conflicts with these defaults. Adjust its thresholds first."
        return updated, ""

    def autosave(self) -> bool:
        """Save now. Returns True when the controls and the saved settings agree."""
        self._timer.stop()
        if self._busy_saving:
            return False
        updated, problem = self.candidate()
        if updated is None:
            self.set_status(problem)
            return False
        if updated == self.controller.settings:
            self.set_status("")
            return True
        self._busy_saving = True
        try:
            self.controller.save_settings(updated)
            self.set_status("")
            return True
        except ValueError as error:
            self.set_status(str(error))
        except Exception:  # noqa: BLE001 - the previous settings stay in force
            self.set_status(SAVE_FAILED)
        finally:
            self._busy_saving = False
        return False

    def before_close(self) -> bool:
        self.autosave()
        return not self.status_text()


# ---------------------------------------------------------------------------------------------- account
class AccountSettingsPanel(_SettingsPanel):
    """Per-account settings. `reconnect(account)` starts the provider sign-in for this account;
    `remove(account)` runs after the user confirms and must persist the removal (raising on failure)."""

    def __init__(self, controller, account_id: str, reconnect: Callable[[ProviderAccount], None],
                 remove: Callable[[ProviderAccount], None]):
        self.account_id = account_id
        account = controller.state.account(account_id)
        if account is None:
            raise KeyError(account_id)
        self._account = account
        super().__init__(controller, self._title(controller, account))
        self._reconnect = reconnect
        self._remove = remove
        self._polling_timer = QTimer(self)
        self._polling_timer.setSingleShot(True)
        self._polling_timer.setInterval(SAVE_DELAY_MS)
        self._polling_timer.timeout.connect(self.save_polling)
        self._syncing = False
        self._build(account)
        controller.changed.connect(self._external_change)
        self.closed.connect(self._detach)

    @staticmethod
    def _title(controller, account: ProviderAccount) -> str:
        snapshot = controller.state.snapshots.get(account.Id)
        return f"{account.Provider} / {account.Nickname} · {plan_label(account.Provider, snapshot.PlanType if snapshot else None)}"

    def latest(self) -> ProviderAccount:
        return self.controller.state.account(self.account_id) or self._account

    # construction ---------------------------------------------------------------------------
    def _build(self, account: ProviderAccount) -> None:
        s = self.controller.settings
        snapshot = self.controller.state.snapshots.get(account.Id)
        if self.demo:
            self.add(label("Demo changes stay in this preview.", 14, T.ACCENT, wrap=True))

        layout = self.card("Account")
        grid = QGridLayout()
        grid.setHorizontalSpacing(12)
        grid.setVerticalSpacing(10)
        grid.setColumnMinimumWidth(0, 190)
        grid.setColumnStretch(1, 1)
        grid.addWidget(label("Account Name", 14), 0, 0)
        self.name = QLineEdit(account.Nickname)
        self.name.setPlaceholderText("Personal, Work")
        self.name.setAccessibleName("Account Name")
        self.name.setMaxLength(80)
        self.name.editingFinished.connect(self.rename)
        grid.addWidget(self.name, 0, 1)

        grid.addWidget(label("Line Color", 14), 1, 0, Qt.AlignmentFlag.AlignTop)
        self.selected_color = account_color(account, plan_label(account.Provider, snapshot.PlanType if snapshot else None)).upper()
        palette = [*LINE_PALETTE, *(s.CustomLineColors or ()), self.selected_color]
        self.palette: list[str] = list(dict.fromkeys(h.upper() for h in palette if _valid_hex(h)))
        self.swatches = FlowRow()
        for hex_color in self.palette:
            self.swatches.append(self._swatch(hex_color))
        self.add_color = AddColorButton()
        self.add_color.clicked.connect(self.pick_color)
        self.swatches.append(self.add_color)
        self._mark_color()
        grid.addWidget(self.swatches, 1, 1)

        polling_label = QWidget()
        polling_row = QHBoxLayout(polling_label)
        polling_row.setContentsMargins(0, 0, 0, 0)
        polling_row.setSpacing(6)
        self.polling_label = label("", 14)
        polling_row.addWidget(self.polling_label)
        polling_row.addWidget(HelpIcon(HELP["Polling Interval"]))
        polling_row.addStretch(1)
        grid.addWidget(polling_label, 2, 0)
        self.polling = MinuteSlider(account.effective_polling_minutes)
        self.polling.valueChanged.connect(self._polling_moved)
        self.polling.released.connect(self.save_polling)
        grid.addWidget(self.polling, 2, 1)
        self._polling_moved(self.polling.value(), save=False)
        layout.addLayout(grid)
        self.enabled = self.toggle(layout, "Monitor This Account", account.Enabled, help_text=True)
        self.enabled.changed.connect(self._enabled_changed)

        if snapshot is not None and snapshot.Windows:
            layout = self.card("Usage Information")
            for window in snapshot.Windows:
                rule = s.rule_for(account.Id, window.Id)
                row = QuotaRow(account, snapshot, window, rule.LowThreshold, rule.CriticalThreshold, rule.InfoThreshold)
                layout.addWidget(label(row.window_title, 16, T.TEXT, wrap=True))
                layout.addWidget(label(f"{row.used_text} · {row.left_text}", 14, T.TEXT_SECONDARY, wrap=True))
                if row.source_note.strip():
                    layout.addWidget(label(row.source_note, 13, T.TEXT_SECONDARY, wrap=True))

        reserve = snapshot.Reserve if snapshot is not None else None
        if reserve is not None:
            layout = self.card("Available Resets")
            count = "Unknown" if reserve.AvailableCount is None else str(reserve.AvailableCount)
            layout.addWidget(label(f"{count} Available", 20, T.TEXT))
            for credit in reserve.Credits or ():
                layout.addWidget(label(f"Expires {format_reset(credit.ExpiresAt, self.controller.zone)}", 14, T.EXPIRY))
            if reserve.has_partial_details:
                layout.addWidget(label("Some reset details are not supplied by the provider.", 12, T.TEXT_SECONDARY, wrap=True))

        layout = self.card("Other Alerts", "Warning and critical levels are adjusted directly on the dashboard bars.")
        rule = s.rule_for(account.Id)
        self.exhausted = self.toggle(layout, "Capacity Exhausted", rule.Exhausted, help_text=True)
        self.restored = self.toggle(layout, "Capacity Restored", rule.Restored, help_text=True)
        self.expiry = self.toggle(layout, "Reset Expiry Reminders", rule.Expiry, help_text=True)
        for row in (self.exhausted, self.restored, self.expiry):
            row.changed.connect(lambda _v: self.save_alerts())

        layout = self.card("Connection")
        layout.addWidget(label("Sign in again only if this account needs a new connection. Your saved settings stay in place.",
                               13, T.TEXT_SECONDARY, wrap=True))
        actions = QHBoxLayout()
        actions.setSpacing(8)
        self.sign_in = ActionButton("Sign In Again", "refresh")
        self.sign_in.setEnabled(not self.demo)
        self.sign_in.clicked.connect(self._sign_in)
        self.remove_button = ActionButton("Remove Account", "trash")
        self.remove_button.setEnabled(not self.demo)
        self.remove_button.clicked.connect(self.confirm_remove)
        actions.addWidget(self.sign_in)
        actions.addWidget(self.remove_button)
        actions.addStretch(1)
        layout.addLayout(actions)
        self.layout_body.addStretch(1)

    def _swatch(self, hex_color: str) -> ColorSwatch:
        swatch = ColorSwatch(hex_color)
        swatch.clicked.connect(lambda _=False, h=swatch.hex: self.choose_color(h))
        return swatch

    def _mark_color(self) -> None:
        for item in self.swatches.items():
            if isinstance(item, ColorSwatch):
                item.set_selected(item.hex == self.selected_color)

    # events ---------------------------------------------------------------------------------
    def _external_change(self) -> None:
        if self.controller.state.account(self.account_id) is None:
            # Removed elsewhere (or demo exited): nothing left to edit.
            if self.host is not None:
                self.host.close_panel(self)
            return
        self.title_label.setText(self._title(self.controller, self.latest()))

    def _polling_moved(self, value: int, save: bool = True) -> None:
        self.polling_label.setText(f"Polling Interval: {value} Min")
        if save:
            self._polling_timer.start()

    def rename(self) -> bool:
        value = self.name.text().strip()
        if not value or value == self.latest().Nickname:
            return True
        try:
            self.controller.update_account(replace(self.latest(), Nickname=value))
            return True
        except Exception:  # noqa: BLE001
            self.account_error()
            return False

    def save_polling(self) -> bool:
        self._polling_timer.stop()
        minutes = self.polling.value()
        try:
            if minutes != self.latest().effective_polling_minutes:
                self.controller.update_account(replace(self.latest(), PollingMinutes=minutes))
            if self.status_text().startswith("Polling"):
                self.set_status("")
            return True
        except Exception:  # noqa: BLE001
            self.set_status("Polling Interval Could Not Be Saved - Try Again")
            return False

    def _enabled_changed(self, on: bool) -> None:
        if self._syncing:
            return
        try:
            self.controller.update_account(replace(self.latest(), Enabled=on))
        except Exception:  # noqa: BLE001
            self._syncing = True
            self.enabled.set_checked(self.latest().Enabled)
            self._syncing = False
            self.account_error()

    def choose_color(self, hex_color: str) -> bool:
        try:
            self.controller.update_account(replace(self.latest(), LineColor=hex_color))
        except Exception:  # noqa: BLE001
            self.set_status("Line Color Could Not Be Saved")
            return False
        self.selected_color = hex_color
        self._mark_color()
        self.set_status("")
        return True

    def add_custom_color(self, hex_color: str) -> bool:
        """Remember a custom color in settings (persisted first), then apply it to this account."""
        hex_color = hex_color.upper()
        if not _valid_hex(hex_color):
            return False
        try:
            if hex_color not in self.palette:
                settings = self.controller.settings
                colors = tuple(dict.fromkeys([*(settings.CustomLineColors or ()), hex_color]))
                self.controller.save_settings(replace(settings, CustomLineColors=colors))
                self.palette.append(hex_color)
                self.swatches.insert(len(self.swatches.items()) - 1, self._swatch(hex_color))
        except Exception:  # noqa: BLE001
            self.set_status("Custom Color Could Not Be Saved")
            return False
        return self.choose_color(hex_color)

    def pick_color(self) -> None:
        picker = QColorDialog(QColor(self.selected_color))
        picker.setOptions(QColorDialog.ColorDialogOption.NoButtons | QColorDialog.ColorDialogOption.DontUseNativeDialog)
        picker.setWindowFlags(Qt.WindowType.Widget)
        if self.host is None:
            return
        dialog = Dialog("Add Line Color", "", (("Add Color", lambda: self.add_custom_color(picker.currentColor().name())),
                                               ("Cancel", None)), max_width=640, extra=picker)
        self.host.show_panel(dialog)

    def save_alerts(self) -> bool:
        try:
            updated = self.controller.settings.with_account_rule(
                self.account_id, Exhausted=self.exhausted.is_checked(), Restored=self.restored.is_checked(),
                Expiry=self.expiry.is_checked())
            self.controller.save_settings(updated)
            self.set_status("")
            return True
        except Exception:  # noqa: BLE001
            self.set_status("Alerts Could Not Be Saved - Try Again")
            return False

    def _sign_in(self) -> None:
        try:
            self._reconnect(self.latest())
        except Exception:  # noqa: BLE001
            self.account_error()

    def confirm_remove(self) -> None:
        if self.host is None:
            return
        confirm(self.host, "Remove Account?",
                "This removes the saved connection from Scrooge. Your provider account is unaffected.",
                "Remove", self.remove_now)

    def remove_now(self) -> None:
        try:
            self._remove(self.latest())
        except Exception:  # noqa: BLE001
            self.account_error()
            return
        if self.host is not None:
            self.host.close_panel(self)

    def before_close(self) -> bool:
        if self.controller.state.account(self.account_id) is None:
            return True
        self.rename()
        self.save_polling()
        return not self.status_text()

    def _detach(self) -> None:
        try:
            self.controller.changed.disconnect(self._external_change)
        except (TypeError, RuntimeError):
            pass
