"""Shared controls. Reuse these rather than styling one-off buttons, switches or bars."""
from __future__ import annotations

from typing import Callable, Optional, Sequence

from PyQt6.QtCore import QPointF, QRectF, QSize, Qt, QTimer, pyqtSignal
from PyQt6.QtGui import QBrush, QColor, QLinearGradient, QPainter, QPainterPath, QPen
from PyQt6.QtWidgets import (QAbstractButton, QFrame, QHBoxLayout, QLabel, QPushButton, QSizePolicy, QToolTip, QVBoxLayout,
                             QWidget)

from ..core.settings import AlertThresholds
from . import theme as T


def label(text: str = "", size: float = 14, color: str = T.TEXT, semibold: bool = False, wrap: bool = False,
          align: Qt.AlignmentFlag | None = None) -> QLabel:
    w = QLabel(text)
    w.setFont(T.font(size, semibold))
    w.setStyleSheet(f"color: {color}; background: transparent;")
    w.setWordWrap(wrap)
    if align is not None:
        w.setAlignment(align)
    return w


def set_label_color(w: QLabel, color: str) -> None:
    w.setStyleSheet(f"color: {color}; background: transparent;")


class Card(QFrame):
    def __init__(self, radius: int = 13, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._radius = radius
        self._highlight = False
        self.setAttribute(Qt.WidgetAttribute.WA_StyledBackground, False)

    def set_highlight(self, on: bool) -> None:
        self._highlight = on
        self.update()

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        width = 3 if self._highlight else 1
        rect = QRectF(self.rect()).adjusted(width / 2, width / 2, -width / 2, -width / 2)
        p.setPen(QPen(QColor(T.HIGHLIGHT if self._highlight else T.CARD_BORDER), width))
        p.setBrush(QColor(T.CARD))
        p.drawRoundedRect(rect, self._radius, self._radius)


class ActionButton(QPushButton):
    """Icon + text action. Hover and processing share the interaction purple with white
    foreground; processing shows an animated ring and blocks repeated activation."""

    def __init__(self, text: str = "", icon_name: Optional[str] = None, icon_only: bool = False,
                 parent: Optional[QWidget] = None, height: int = 44):
        super().__init__(text, parent)
        self._icon = icon_name
        self._icon_only = icon_only
        self._selected = False
        self._busy_label: Optional[str] = None
        self._angle = 0
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._spin)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setFont(T.font(14))
        self.setFixedHeight(height)
        self.setMouseTracking(True)
        self.setAttribute(Qt.WidgetAttribute.WA_Hover)
        if icon_only:
            self.setFixedWidth(height)
            self.setToolTip(text)
            self.setAccessibleName(text)

    def sizeHint(self) -> QSize:
        if self._icon_only:
            return QSize(self.height(), self.height())
        fm = self.fontMetrics()
        text = self._busy_label or self.text()
        return QSize(fm.horizontalAdvance(text) + (56 if self._icon or self._busy_label else 32), self.height())

    def set_selected(self, on: bool) -> None:
        self._selected = on
        self.update()

    @property
    def busy(self) -> bool:
        return self._busy_label is not None

    def set_busy(self, label_text: Optional[str]) -> None:
        """Show processing immediately, before any expensive work is started."""
        self._busy_label = label_text
        if label_text:
            self._timer.start(30)
        else:
            self._timer.stop()
        self.updateGeometry()
        self.repaint()

    def _spin(self):
        self._angle = (self._angle + 12) % 360
        self.update()

    def mousePressEvent(self, e):
        if self.busy:
            return
        super().mousePressEvent(e)

    def mouseReleaseEvent(self, e):
        if self.busy:
            return
        super().mouseReleaseEvent(e)

    def keyPressEvent(self, e):
        if self.busy:
            return
        super().keyPressEvent(e)

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        hot = (self.underMouse() and self.isEnabled()) or self.isDown() or self.busy
        fill = T.INTERACTION if hot or self._selected else T.ACTION
        rect = QRectF(self.rect()).adjusted(0.5, 0.5, -0.5, -0.5)
        p.setPen(QPen(QColor(T.ACTION_BORDER if not hot else T.INTERACTION), 1))
        p.setBrush(QColor(fill))
        p.drawRoundedRect(rect, 10, 10)
        if self.hasFocus() and not hot:
            p.setPen(QPen(QColor(T.ACCENT), 1.5))
            p.setBrush(Qt.BrushStyle.NoBrush)
            p.drawRoundedRect(rect.adjusted(1.5, 1.5, -1.5, -1.5), 9, 9)
        fg = "#FFFFFF" if hot else (T.TEXT if self.isEnabled() else T.TEXT_TERTIARY)
        icon_color = "#FFFFFF" if hot else T.ACCENT
        text = self._busy_label or self.text()
        size = 20
        if self._icon_only:
            r = QRectF((self.width() - size) / 2, (self.height() - size) / 2, size, size)
            self._draw_glyph(p, r, icon_color)
            return
        fm = self.fontMetrics()
        has_glyph = bool(self._icon) or self.busy
        total = fm.horizontalAdvance(text) + (size + 10 if has_glyph else 0)
        x = (self.width() - total) / 2
        if has_glyph:
            self._draw_glyph(p, QRectF(x, (self.height() - size) / 2, size, size), icon_color)
            x += size + 10
        p.setPen(QColor(fg))
        p.setFont(self.font())
        p.drawText(QRectF(x, 0, self.width() - x, self.height()), Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignLeft, text)

    def _draw_glyph(self, p: QPainter, r: QRectF, color: str) -> None:
        if self.busy:
            pen = QPen(QColor("#FFFFFF"), 2.2)
            pen.setCapStyle(Qt.PenCapStyle.RoundCap)
            p.setPen(pen)
            p.setBrush(Qt.BrushStyle.NoBrush)
            p.drawArc(r.adjusted(2, 2, -2, -2), -self._angle * 16, 270 * 16)
        elif self._icon:
            T.draw_icon(p, self._icon, r, color)


class ToggleSwitch(QAbstractButton):
    toggled_by_user = pyqtSignal(bool)

    def __init__(self, checked: bool = False, parent: Optional[QWidget] = None, on_color: str = T.SWITCH_ON):
        super().__init__(parent)
        self.setCheckable(True)
        self.setChecked(checked)
        self._on = on_color
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setFixedSize(44, 24)
        self.clicked.connect(lambda: self.toggled_by_user.emit(self.isChecked()))

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        on = self.isChecked()
        alpha = 255 if self.isEnabled() else 110
        track = QColor(self._on if on else T.ACTION)
        track.setAlpha(alpha)
        p.setPen(QPen(QColor(self._on if on else T.TEXT_DETAIL), 1.2))
        p.setBrush(track)
        p.drawRoundedRect(QRectF(0.5, 0.5, 43, 23), 11.5, 11.5)
        knob = QColor("#FFFFFF" if on else T.TEXT_DETAIL)
        knob.setAlpha(alpha)
        p.setPen(Qt.PenStyle.NoPen)
        p.setBrush(knob)
        x = 24 if on else 5
        p.drawEllipse(QRectF(x, 5, 14, 14))
        if self.hasFocus():
            p.setPen(QPen(QColor(T.ACCENT), 1.5))
            p.setBrush(Qt.BrushStyle.NoBrush)
            p.drawRoundedRect(QRectF(-0.5, -0.5, 45, 25), 12.5, 12.5)


class SwitchRow(QWidget):
    changed = pyqtSignal(bool)

    def __init__(self, title: str, checked: bool, help_text: Optional[str] = None, parent: Optional[QWidget] = None):
        super().__init__(parent)
        row = QHBoxLayout(self)
        row.setContentsMargins(0, 2, 0, 2)
        self.title = label(title, 14)
        row.addWidget(self.title)
        if help_text:
            row.addWidget(HelpIcon(help_text))
        row.addStretch(1)
        self.switch = ToggleSwitch(checked)
        self.switch.setAccessibleName(title)
        self.switch.toggled_by_user.connect(self.changed)
        row.addWidget(self.switch)

    def is_checked(self) -> bool:
        return self.switch.isChecked()

    def set_checked(self, on: bool) -> None:
        self.switch.setChecked(on)


class HelpIcon(QWidget):
    def __init__(self, text: str, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.setFixedSize(20, 20)
        self.setToolTip(text)
        self.setAccessibleDescription(text)

    def enterEvent(self, e):
        QToolTip.showText(self.mapToGlobal(self.rect().bottomLeft()), self.toolTip(), self)

    def paintEvent(self, e):
        p = QPainter(self)
        T.draw_icon(p, "info", QRectF(1, 1, 18, 18), T.ACCENT)


class PlanBadge(QLabel):
    """Pill badge. Painted directly because Qt drops stylesheet radii above half the height."""

    def __init__(self, text: str, parent: Optional[QWidget] = None, size: float = 12):
        super().__init__(text, parent)
        self.setFont(T.font(size))
        self.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self.setSizePolicy(QSizePolicy.Policy.Maximum, QSizePolicy.Policy.Fixed)

    def sizeHint(self) -> QSize:
        fm = self.fontMetrics()
        return QSize(fm.horizontalAdvance(self.text()) + 24, fm.height() + 8)

    def minimumSizeHint(self) -> QSize:
        return self.sizeHint()

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        r = QRectF(self.rect()).adjusted(0.5, 0.5, -0.5, -0.5)
        p.setPen(QPen(QColor(T.BADGE_BORDER), 1))
        p.setBrush(QColor(T.BADGE_BG))
        p.drawRoundedRect(r, r.height() / 2, r.height() / 2)
        p.setPen(QColor(T.HIGHLIGHT))
        p.setFont(self.font())
        p.drawText(r, Qt.AlignmentFlag.AlignCenter, self.text())


class SegmentedControl(QWidget):
    """Equal segments with the shared selected fill. Selection remains after pointer exit."""
    selected = pyqtSignal(object)

    def __init__(self, options: Sequence[tuple[object, str, Optional[str]]], current: object,
                 parent: Optional[QWidget] = None, height: int = 44):
        super().__init__(parent)
        self._options = list(options)
        self._current = current
        self._hover = -1
        self.setFixedHeight(height)
        self.setMouseTracking(True)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)
        self.setFont(T.font(14))
        self.setAccessibleName("Account Sort")

    def set_current(self, key: object) -> None:
        self._current = key
        self.update()

    def _index_at(self, x: float) -> int:
        n = len(self._options)
        return max(0, min(n - 1, int(x / (self.width() / n))))

    def mouseMoveEvent(self, e):
        self._hover = self._index_at(e.position().x())
        self.update()

    def leaveEvent(self, e):
        self._hover = -1
        self.update()

    def mouseReleaseEvent(self, e):
        key = self._options[self._index_at(e.position().x())][0]
        if key != self._current:
            self._current = key
            self.update()
            self.selected.emit(key)

    def keyPressEvent(self, e):
        keys = [o[0] for o in self._options]
        i = keys.index(self._current) if self._current in keys else 0
        if e.key() in (Qt.Key.Key_Right, Qt.Key.Key_Down):
            i = min(len(keys) - 1, i + 1)
        elif e.key() in (Qt.Key.Key_Left, Qt.Key.Key_Up):
            i = max(0, i - 1)
        else:
            return super().keyPressEvent(e)
        self._current = keys[i]
        self.update()
        self.selected.emit(self._current)

    def sizeHint(self) -> QSize:
        fm = self.fontMetrics()
        return QSize(sum(fm.horizontalAdvance(o[1]) + 70 for o in self._options), self.height())

    def minimumSizeHint(self) -> QSize:
        return QSize(len(self._options) * 60, self.height())

    def paintEvent(self, e):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        rect = QRectF(self.rect()).adjusted(0.5, 0.5, -0.5, -0.5)
        clip = QPainterPath()
        clip.addRoundedRect(rect, 10, 10)
        p.setClipPath(clip)
        p.fillRect(rect, QColor(T.ACTION))
        n = len(self._options)
        w = self.width() / n
        compact = w < 150
        for i, (key, text, icon_name) in enumerate(self._options):
            seg = QRectF(i * w, 0, w, self.height())
            hot = i == self._hover
            if key == self._current or hot:
                p.fillRect(seg, QColor(T.INTERACTION))
            if i:
                p.setPen(QPen(QColor(T.ACTION_BORDER), 1))
                p.drawLine(QPointF(seg.left(), 0), QPointF(seg.left(), self.height()))
            fm = self.fontMetrics()
            shown = text if not compact else text.split()[0]
            total = fm.horizontalAdvance(shown) + (28 if icon_name else 0)
            x = seg.left() + (w - total) / 2
            if icon_name:
                T.draw_icon(p, icon_name, QRectF(x, (self.height() - 18) / 2, 18, 18), "#FFFFFF" if hot else T.ACCENT)
                x += 28
            p.setPen(QColor("#FFFFFF" if hot else T.TEXT))
            p.drawText(QRectF(x, 0, w, self.height()), Qt.AlignmentFlag.AlignVCenter, shown)
        p.setClipping(False)
        p.setPen(QPen(QColor(T.ACTION_BORDER), 1))
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawRoundedRect(rect, 10, 10)


class AlarmToggle(QWidget):
    """Two-part bell / muted-bell control for one quota row. The selected half stays purple."""
    changed = pyqtSignal(bool)

    def __init__(self, enabled: bool, description: str, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self._enabled = enabled
        self._hover = -1
        self.setFixedSize(80, 44)
        self.setMouseTracking(True)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setToolTip(description)
        self.setAccessibleName(description)
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)

    def set_enabled_state(self, on: bool) -> None:
        self._enabled = on
        self.update()

    def mouseMoveEvent(self, e):
        self._hover = 0 if e.position().x() < 40 else 1
        self.update()

    def leaveEvent(self, e):
        self._hover = -1
        self.update()

    def mouseReleaseEvent(self, e):
        want = e.position().x() < 40
        if want != self._enabled:
            self._enabled = want
            self.update()
            self.changed.emit(want)

    def keyPressEvent(self, e):
        if e.key() in (Qt.Key.Key_Space, Qt.Key.Key_Return):
            self._enabled = not self._enabled
            self.update()
            self.changed.emit(self._enabled)
        else:
            super().keyPressEvent(e)

    def paintEvent(self, e):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        rect = QRectF(self.rect()).adjusted(0.5, 0.5, -0.5, -0.5)
        clip = QPainterPath()
        clip.addRoundedRect(rect, 10, 10)
        p.setClipPath(clip)
        p.fillRect(rect, QColor(T.ACTION))
        selected = 0 if self._enabled else 1
        for i in (0, 1):
            seg = QRectF(i * 40, 0, 40, 44)
            if i == selected or i == self._hover:
                p.fillRect(seg, QColor(T.INTERACTION))
            name = "alarm_on.svg" if i == 0 else "alarm_off.svg"
            p.drawPixmap(QRectF(seg.center().x() - 10, 12, 20, 20).toRect(),
                         T.pixmap(name, 20, "#FFFFFF" if i == self._hover else None))
        p.setClipping(False)
        p.setPen(QPen(QColor(T.ACCENT if self.hasFocus() else T.ACTION_BORDER), 1))
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawRoundedRect(rect, 10, 10)
        p.drawLine(QPointF(40, 0), QPointF(40, 44))


class UsageBar(QWidget):
    """Fills by percent used (Used left, Left right). Threshold handles show *remaining* levels, so
    Info 50% / Warning 30% / Critical 10% left sit at 50/70/90% of the used track. Dragging a handle
    never opens details; clicking elsewhere does."""
    clicked = pyqtSignal()
    thresholds_changed = pyqtSignal(object)  # AlertThresholds after a drag completes
    interaction = pyqtSignal(bool)

    HANDLE_GRAB = 9

    def __init__(self, fill: float, used: str, left: str, severity: str, thresholds: AlertThresholds,
                 alerts_enabled: bool, description: str, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.fill = fill
        self.used, self.left = used, left
        self.severity = severity
        self.thresholds = thresholds
        self.alerts_enabled = alerts_enabled
        self._drag: Optional[str] = None
        self._hover: Optional[str] = None
        self._pressed = False
        self.setFixedHeight(48)
        self.setMinimumWidth(220)
        self.setMouseTracking(True)
        self.setCursor(Qt.CursorShape.PointingHandCursor)
        self.setAccessibleName(f"{description}, {used}, {left}")
        self.setFocusPolicy(Qt.FocusPolicy.StrongFocus)

    def set_state(self, severity: str, thresholds: AlertThresholds, alerts_enabled: bool) -> None:
        self.severity, self.thresholds, self.alerts_enabled = severity, thresholds, alerts_enabled
        self.update()

    def _x_for(self, remaining: float) -> float:
        return 1 + (self.width() - 2) * (1 - remaining / 100)

    def _handles(self) -> list[tuple[str, float, str]]:
        t = self.thresholds
        return [("info", t.Info, T.INFO), ("warning", t.Warning, T.WARNING), ("critical", t.Critical, T.CRITICAL)]

    def _handle_at(self, x: float) -> Optional[str]:
        if not self.alerts_enabled:
            return None
        best = None
        for name, value, _ in self._handles():
            d = abs(self._x_for(value) - x)
            if d <= self.HANDLE_GRAB and (best is None or d < best[1]):
                best = (name, d)
        return best[0] if best else None

    def mousePressEvent(self, e):
        self._drag = self._handle_at(e.position().x())
        self._pressed = True
        if self._drag:
            self.interaction.emit(True)

    def mouseMoveEvent(self, e):
        x = e.position().x()
        if self._drag:
            remaining = AlertThresholds.remaining_at(x - 1, self.width() - 2)
            t = self.thresholds
            if self._drag == "info":
                t = t.move_info(remaining)
            else:
                t = t.move(self._drag == "warning", remaining)
            self.thresholds = t
            value = {"info": t.Info, "warning": t.Warning, "critical": t.Critical}[self._drag]
            QToolTip.showText(e.globalPosition().toPoint(), f"{self._drag.title()} at {value:.0f}% Left", self)
            self.update()
            return
        hover = self._handle_at(x)
        if hover != self._hover:
            self._hover = hover
            self.setCursor(Qt.CursorShape.SizeHorCursor if hover else Qt.CursorShape.PointingHandCursor)
            self.update()

    def mouseReleaseEvent(self, e):
        if self._drag:
            self._drag = None
            QToolTip.hideText()
            self.interaction.emit(False)
            self.thresholds_changed.emit(self.thresholds)
        elif self._pressed and self.rect().contains(e.position().toPoint()):
            self.clicked.emit()
        self._pressed = False
        self.update()

    def leaveEvent(self, e):
        self._hover = None
        self.update()

    def keyPressEvent(self, e):
        if e.key() in (Qt.Key.Key_Return, Qt.Key.Key_Space):
            self.clicked.emit()
        else:
            super().keyPressEvent(e)

    def paintEvent(self, e):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        rect = QRectF(self.rect()).adjusted(0.5, 0.5, -0.5, -0.5)
        clip = QPainterPath()
        clip.addRoundedRect(rect, 11, 11)
        p.setClipPath(clip)
        p.fillRect(rect, QColor(T.TRACK))
        if self.fill > 0:
            start, end = T.GRADIENTS[self.severity]
            grad = QLinearGradient(0, 0, max(1.0, rect.width() * self.fill), 0)
            grad.setColorAt(0, QColor(start))
            grad.setColorAt(1, QColor(end))
            fill_path = QPainterPath()
            fill_path.addRoundedRect(QRectF(rect.left(), rect.top(), rect.width() * self.fill, rect.height()), 11, 11)
            p.fillPath(fill_path, QBrush(grad))
        p.setClipping(False)
        if self.alerts_enabled:
            for name, value, color in self._handles():
                x = self._x_for(value)
                hot = name in (self._hover, self._drag)
                w = 8 if hot else 5
                h = 30 if hot else 24
                r = QRectF(x - w / 2, (self.height() - h) / 2, w, h)
                p.setPen(QPen(QColor(T.CANVAS), 1.5))
                p.setBrush(QColor(color))
                p.drawRoundedRect(r, w / 2, w / 2)
        p.setPen(QPen(QColor(T.ACCENT if self.hasFocus() else T.TRACK_BORDER), 1))
        p.setBrush(Qt.BrushStyle.NoBrush)
        p.drawRoundedRect(rect, 11, 11)
        p.setPen(QColor(T.TEXT))
        p.setFont(T.font(16, True))
        inner = QRectF(rect).adjusted(15, 0, -15, 0)
        p.drawText(inner, Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignLeft, self.used)
        p.setFont(T.font(16))
        p.drawText(inner, Qt.AlignmentFlag.AlignVCenter | Qt.AlignmentFlag.AlignRight, self.left)


class Spinner(QWidget):
    def __init__(self, size: int = 18, parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.setFixedSize(size, size)
        self._angle = 0
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._tick)

    def start(self):
        self._timer.start(30)
        self.show()

    def stop(self):
        self._timer.stop()
        self.hide()

    def _tick(self):
        self._angle = (self._angle + 12) % 360
        self.update()

    def paintEvent(self, e):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        pen = QPen(QColor(T.ACCENT), 2.2)
        pen.setCapStyle(Qt.PenCapStyle.RoundCap)
        p.setPen(pen)
        p.drawArc(QRectF(self.rect()).adjusted(2, 2, -2, -2), -self._angle * 16, 270 * 16)


def hline(color: str = T.CARD_BORDER) -> QFrame:
    line = QFrame()
    line.setFixedHeight(1)
    line.setStyleSheet(f"background: {color}; border: none;")
    return line


def vbox(*widgets, spacing: int = 8, margins=(0, 0, 0, 0)) -> QVBoxLayout:
    layout = QVBoxLayout()
    layout.setSpacing(spacing)
    layout.setContentsMargins(*margins)
    for w in widgets:
        layout.addWidget(w) if isinstance(w, QWidget) else layout.addLayout(w)
    return layout


def hbox(*widgets, spacing: int = 8, margins=(0, 0, 0, 0)) -> QHBoxLayout:
    layout = QHBoxLayout()
    layout.setSpacing(spacing)
    layout.setContentsMargins(*margins)
    for w in widgets:
        if w is None:
            layout.addStretch(1)
        elif isinstance(w, QWidget):
            layout.addWidget(w)
        else:
            layout.addLayout(w)
    return layout


def severity_key(capacity) -> str:
    from ..core.model import CapacityState
    if capacity in (CapacityState.Critical, CapacityState.Exhausted):
        return "critical"
    if capacity == CapacityState.Low:
        return "warning"
    if capacity == CapacityState.Info:
        return "info"
    return "normal"
