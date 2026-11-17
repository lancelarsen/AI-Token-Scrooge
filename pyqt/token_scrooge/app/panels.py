"""Large panels centered inside the main window over a dimmed dashboard, plus themed small
dialogs. Panels cap at 1000x800 with at least 24/32 units of margin; Escape follows Close."""
from __future__ import annotations

from typing import Callable, Optional, Sequence

from PyQt6.QtCore import QEvent, QRect, Qt, pyqtSignal
from PyQt6.QtGui import QColor, QKeyEvent, QPainter, QPen
from PyQt6.QtWidgets import QFrame, QHBoxLayout, QScrollArea, QSizePolicy, QVBoxLayout, QWidget

from . import theme as T
from .widgets import ActionButton, hline, label


class Panel(QFrame):
    """Fixed title/Close row with a scrolling body. Subclasses may override `before_close` to flush
    pending edits; returning False keeps the panel open (for example after a failed save)."""
    closed = pyqtSignal()

    def __init__(self, title: str, max_width: int = 1000, max_height: int = 800, body_width: int = 960,
                 close_text: str = "Close", parent: Optional[QWidget] = None):
        super().__init__(parent)
        self.max_width, self.max_height = max_width, max_height
        self.host: Optional["PanelHost"] = None
        self.setObjectName("Panel")
        outer = QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(0)
        header = QHBoxLayout()
        header.setContentsMargins(24, 18, 18, 14)
        self.title_label = label(title, 26, T.TEXT, True)
        self.title_label.setWordWrap(True)
        header.addWidget(self.title_label, 1)
        self.close_button = ActionButton(close_text, "close")
        self.close_button.clicked.connect(self.request_close)
        header.addWidget(self.close_button, 0, Qt.AlignmentFlag.AlignTop)
        outer.addLayout(header)
        outer.addWidget(hline())
        self.scroll = QScrollArea()
        self.scroll.setWidgetResizable(True)
        self.scroll.setFrameShape(QFrame.Shape.NoFrame)
        self.scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        holder = QWidget()
        holder_layout = QHBoxLayout(holder)
        holder_layout.setContentsMargins(16, 12, 16, 20)
        self.body = QWidget()
        self.body.setMaximumWidth(body_width)
        self.layout_body = QVBoxLayout(self.body)
        self.layout_body.setContentsMargins(0, 0, 0, 0)
        self.layout_body.setSpacing(12)
        holder_layout.addStretch(0)
        holder_layout.addWidget(self.body, 1)
        holder_layout.addStretch(0)
        self.scroll.setWidget(holder)
        outer.addWidget(self.scroll, 1)

    def add(self, widget: QWidget) -> QWidget:
        self.layout_body.addWidget(widget)
        return widget

    def before_close(self) -> bool:
        return True

    def request_close(self) -> None:
        if self.before_close() and self.host is not None:
            self.host.close_panel(self)

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(QPen(QColor(T.CARD_BORDER), 1))
        p.setBrush(QColor(T.CANVAS))
        p.drawRoundedRect(self.rect().adjusted(0, 0, -1, -1), 15, 15)


class PanelHost(QWidget):
    """Overlay that covers the window, dims it, and blocks dashboard input while a panel is open."""
    opened_changed = pyqtSignal(bool)

    def __init__(self, parent: QWidget):
        super().__init__(parent)
        self._stack: list[QWidget] = []
        self._focus_return: list[Optional[QWidget]] = []
        self.hide()
        parent.installEventFilter(self)

    @property
    def is_open(self) -> bool:
        return bool(self._stack)

    def top(self) -> Optional[QWidget]:
        return self._stack[-1] if self._stack else None

    def eventFilter(self, obj, event):
        if obj is self.parent() and event.type() == QEvent.Type.Resize:
            self.setGeometry(self.parent().rect())
            self._place()
        return False

    def show_panel(self, panel: QWidget) -> None:
        from PyQt6.QtWidgets import QApplication
        self._focus_return.append(QApplication.focusWidget())
        panel.setParent(self)
        if isinstance(panel, (Panel, Dialog)):
            panel.host = self
        for other in self._stack:
            other.hide()
        self._stack.append(panel)
        self.setGeometry(self.parent().rect())
        self.show()
        self.raise_()
        panel.show()
        self._place()
        panel.setFocus()
        self.opened_changed.emit(True)

    def close_panel(self, panel: Optional[QWidget] = None) -> None:
        panel = panel or self.top()
        if panel is None or panel not in self._stack:
            return
        index = self._stack.index(panel)
        self._stack.pop(index)
        focus = self._focus_return.pop(index) if index < len(self._focus_return) else None
        panel.hide()
        panel.deleteLater()
        if isinstance(panel, (Panel, Dialog)):
            panel.closed.emit()
        if self._stack:
            self._stack[-1].show()
            self._place()
        else:
            self.hide()
            self.opened_changed.emit(False)
        if focus is not None:
            try:
                focus.setFocus()
            except RuntimeError:
                pass

    def close_all(self) -> None:
        while self._stack:
            self.close_panel(self._stack[-1])

    def _place(self) -> None:
        panel = self.top()
        if panel is None:
            return
        max_w = getattr(panel, "max_width", 1000)
        max_h = getattr(panel, "max_height", 800)
        w = min(max_w, self.width() - 48)
        if isinstance(panel, Dialog):
            h = min(panel.sizeHint().height(), self.height() - 64)
        else:
            h = min(max_h, self.height() - 64)
        panel.setGeometry(QRect((self.width() - w) // 2, (self.height() - h) // 2, max(200, w), max(120, h)))

    def paintEvent(self, event):
        p = QPainter(self)
        p.fillRect(self.rect(), T.SCRIM)

    def mousePressEvent(self, event):
        event.accept()  # Dashboard behind the scrim cannot receive input.

    def keyPressEvent(self, event: QKeyEvent):
        if event.key() == Qt.Key.Key_Escape and self.top() is not None:
            top = self.top()
            if hasattr(top, "request_close"):
                top.request_close()
            else:
                self.close_panel(top)
            return
        super().keyPressEvent(event)


class Dialog(QFrame):
    """Themed small dialog for details, choices and confirmation."""
    closed = pyqtSignal()

    def __init__(self, title: str, text: str = "", buttons: Sequence[tuple[str, Optional[Callable[[], None]]]] = (("Close", None),),
                 max_width: int = 560, parent: Optional[QWidget] = None, extra: Optional[QWidget] = None):
        super().__init__(parent)
        self.max_width = max_width
        self.host: Optional[PanelHost] = None
        layout = QVBoxLayout(self)
        layout.setContentsMargins(24, 22, 24, 20)
        layout.setSpacing(14)
        layout.addWidget(label(title, 21, T.TEXT, True, wrap=True))
        if text:
            body = label(text, 14, T.TEXT_SECONDARY, wrap=True)
            body.setTextInteractionFlags(Qt.TextInteractionFlag.TextSelectableByMouse)
            layout.addWidget(body)
        if extra is not None:
            layout.addWidget(extra)
        row = QHBoxLayout()
        row.addStretch(1)
        self._cancel: Optional[Callable[[], None]] = None
        for i, (text_value, action) in enumerate(buttons):
            button = ActionButton(text_value)
            button.setMinimumWidth(110)
            button.clicked.connect(lambda _=False, a=action: self._choose(a))
            row.addWidget(button)
            if i == len(buttons) - 1 and len(buttons) > 1:
                self._cancel = action
        layout.addLayout(row)
        self.setSizePolicy(QSizePolicy.Policy.Preferred, QSizePolicy.Policy.Maximum)

    def _choose(self, action: Optional[Callable[[], None]]) -> None:
        if self.host is not None:
            self.host.close_panel(self)
        if action:
            action()

    def request_close(self) -> None:
        self._choose(self._cancel)

    def paintEvent(self, event):
        p = QPainter(self)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(QPen(QColor(T.POPUP_BORDER), 1))
        p.setBrush(QColor(T.DIALOG))
        p.drawRoundedRect(self.rect().adjusted(0, 0, -1, -1), 14, 14)


def message(host: PanelHost, title: str, text: str, button: str = "Close", extra: Optional[QWidget] = None,
            buttons: Optional[Sequence[tuple[str, Optional[Callable[[], None]]]]] = None) -> Dialog:
    dialog = Dialog(title, text, buttons or ((button, None),), extra=extra)
    host.show_panel(dialog)
    return dialog


def confirm(host: PanelHost, title: str, text: str, ok: str, on_ok: Callable[[], None], cancel: str = "Cancel") -> Dialog:
    dialog = Dialog(title, text, ((ok, on_ok), (cancel, None)))
    host.show_panel(dialog)
    return dialog


def section(title: str, help_text: Optional[str] = None, header_widget: Optional[QWidget] = None) -> tuple[QFrame, QVBoxLayout]:
    """Settings card: 15-unit corners, 16 padding, 21-unit heading, optional section-header tooltip."""
    from .widgets import Card, HelpIcon
    card = Card(15)
    layout = QVBoxLayout(card)
    layout.setContentsMargins(16, 14, 16, 16)
    layout.setSpacing(10)
    head = QHBoxLayout()
    head.setSpacing(8)
    head.addWidget(label(title, 21, T.HIGHLIGHT, True))
    if help_text:
        head.addWidget(HelpIcon(help_text))
    head.addStretch(1)
    if header_widget is not None:
        head.addWidget(header_widget)
    layout.addLayout(head)
    return card, layout
