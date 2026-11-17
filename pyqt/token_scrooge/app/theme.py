"""Palette, typography and icons. Values come from conversion-for-mac/design-reference.md;
choose an existing role before adding a color."""
from __future__ import annotations

import re
import sys
from functools import lru_cache
from importlib import resources

from PyQt6.QtCore import QByteArray, QRectF, QSize, Qt
from PyQt6.QtGui import QColor, QFont, QIcon, QPainter, QPainterPath, QPen, QPixmap
from PyQt6.QtSvg import QSvgRenderer

CANVAS = "#141519"
GRADIENT_START = "#1B1C20"
CARD = "#1C1E24"
CARD_BORDER = "#383B46"
HEADER_DIVIDER = "#363842"
DIALOG = "#222329"
ACTION = "#232429"
ACTION_BORDER = "#45464D"
FIELD = "#2A2B30"
INTERACTION = "#483A64"  # Shared selected/hover/processing fill. Never #48306F.
TEXT = "#F2F1F5"
TEXT_SECONDARY = "#B6B8C8"
TEXT_TERTIARY = "#A5A8BA"
TEXT_DETAIL = "#BEC1D2"
ACCENT = "#B5A0F4"
HIGHLIGHT = "#C3ADF8"
TODAY = "#D1BEFF"
COUNTDOWN = "#BDA3F5"
EXPIRY = "#EAAA51"
INFO = "#58A6FF"
WARNING = "#F2C94C"
CRITICAL = "#F05B69"
SWITCH_ON = "#8170CF"
SLIDER_FILL = "#A990ED"
SLIDER_TRACK = "#41434F"
BADGE_BG = "#2C293E"
BADGE_BORDER = "#51476D"
TRACK = "#30333E"
TRACK_BORDER = "#515461"
RAIL = "#8170B7"
GRID_DAY = "#30333C"
GRID_PERCENT = "#343741"
REFRESH_TILE = "#352A50"
EXPIRY_TILE = "#49321D"
SELECTOR_BG = "#272331"
SAMPLE_BG = "#202229"
NAV_TODAY = "#E5DCF9"
POPUP_BORDER = "#65577F"
DEFAULT_LINE = "#B99AF8"
SCRIM = QColor(0, 0, 0, 160)

GRADIENTS = {  # capacity severity: (start, end)
    "normal": ("#208455", "#4DCE8A"),
    "info": ("#2367B5", "#58A6FF"),
    "warning": ("#AE8519", "#F2C94C"),
    "critical": ("#B52D42", "#F05B69"),
}

SEVERITY_COLORS = {0: INFO, 1: WARNING, 2: CRITICAL}


def font(size: float, semibold: bool = False) -> QFont:
    f = QFont()
    if sys.platform == "darwin":
        f.setFamilies([".AppleSystemUIFont", "Helvetica Neue"])
    else:
        f.setFamilies(["Inter", "Cantarell", "Noto Sans", "DejaVu Sans", "Sans Serif"])
    f.setPointSizeF(size * 0.75)  # Reference sizes are Windows logical px.
    if semibold:
        f.setWeight(QFont.Weight.DemiBold)
    return f


def resource_bytes(*parts: str) -> bytes:
    return resources.files("token_scrooge.resources").joinpath(*parts).read_bytes()


def resource_path(*parts: str) -> str:
    return str(resources.files("token_scrooge.resources").joinpath(*parts))


@lru_cache(maxsize=256)
def _svg(name: str, color: str | None) -> bytes:
    data = resource_bytes("images", name).decode()
    if color:
        data = re.sub(r'(stroke|fill)="#[0-9A-Fa-f]{3,6}"', lambda m: f'{m.group(1)}="{color}"', data)
    return data.encode()


def pixmap(name: str, size: int, color: str | None = None, ratio: float = 2.0) -> QPixmap:
    """Provider logos and bundled icons, rendered crisp at device pixel ratio."""
    if name.endswith(".svg"):
        renderer = QSvgRenderer(QByteArray(_svg(name, color)))
        pm = QPixmap(int(size * ratio), int(size * ratio))
        pm.fill(Qt.GlobalColor.transparent)
        painter = QPainter(pm)
        renderer.render(painter)
        painter.end()
    else:
        pm = QPixmap()
        pm.loadFromData(resource_bytes("images", name))
        pm = pm.scaled(int(size * ratio), int(size * ratio), Qt.AspectRatioMode.KeepAspectRatio,
                       Qt.TransformationMode.SmoothTransformation)
    pm.setDevicePixelRatio(ratio)
    return pm


def provider_image(provider: str) -> str:
    return {"OpenAI": "openai.png", "Devin": "devin.svg", "Claude": "claude.svg", "GitHub Copilot": "github.svg"}.get(provider, "token_baron.png")


def provider_pixmap(provider: str, size: int) -> QPixmap:
    return pixmap(provider_image(provider), size)


# ---------------------------------------------------------------- vector utility icons
# Lucide-style strokes drawn with QPainterPath so they match on every platform without glyph fonts.

_ICON_PATHS = {
    "refresh": "M21 12a9 9 0 1 1-2.64-6.36L21 8M21 3v5h-5",
    "settings": "M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z",
    "plus": "M12 5v14M5 12h14",
    "clock": "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 7v5l3 2",
    "history": "M3 12a9 9 0 1 0 3-6.7L3 8M3 3v5h5M12 7v5l4 2",
    "exit": "M15 3h6v6M10 14 21 3M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",
    "demo": "M9 3h6M10 3v6L4 19a2 2 0 0 0 1.7 3h12.6A2 2 0 0 0 20 19l-6-10V3",
    "close": "M18 6 6 18M6 6l12 12",
    "trash": "M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14",
    "info": "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 16v-5M12 8h.01",
    "check": "M5 12l5 5L20 7",
    "most": "M3 7h18v10H3zM5 9h12v6H5z",
    "least": "M3 7h18v10H3zM5 9h4v6H5z",
    "copy": "M9 9h11v11H9zM5 15H4V4h11v1",
    "link": "M15 3h6v6M10 14 21 3M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6",
}


def _parse_path(d: str) -> QPainterPath:
    path = QPainterPath()
    tokens = re.findall(r"[MLHVCAZmlhvcaz]|-?\d*\.?\d+(?:e-?\d+)?", d)
    i, cmd, x, y, sx, sy = 0, "M", 0.0, 0.0, 0.0, 0.0

    def num():
        nonlocal i
        v = float(tokens[i])
        i += 1
        return v
    while i < len(tokens):
        if re.fullmatch(r"[A-Za-z]", tokens[i]):
            cmd = tokens[i]
            i += 1
            if cmd in "Zz":
                path.closeSubpath()
                x, y = sx, sy
                continue
        rel = cmd.islower()
        c = cmd.upper()
        if c == "M":
            nx, ny = num(), num()
            x, y = (x + nx, y + ny) if rel else (nx, ny)
            path.moveTo(x, y)
            sx, sy = x, y
            cmd = "l" if rel else "L"
        elif c == "L":
            nx, ny = num(), num()
            x, y = (x + nx, y + ny) if rel else (nx, ny)
            path.lineTo(x, y)
        elif c == "H":
            nx = num()
            x = x + nx if rel else nx
            path.lineTo(x, y)
        elif c == "V":
            ny = num()
            y = y + ny if rel else ny
            path.lineTo(x, y)
        elif c == "C":
            pts = [num() for _ in range(6)]
            if rel:
                pts = [pts[k] + (x if k % 2 == 0 else y) for k in range(6)]
            path.cubicTo(*pts)
            x, y = pts[4], pts[5]
        elif c == "A":
            rx, ry, _rot, large, sweep, nx, ny = (num() for _ in range(7))
            ex, ey = (x + nx, y + ny) if rel else (nx, ny)
            _arc_to(path, x, y, rx, ry, bool(large), bool(sweep), ex, ey)
            x, y = ex, ey
        else:
            i += 1
    return path


def _arc_to(path: QPainterPath, x1, y1, rx, ry, large, sweep, x2, y2) -> None:
    import math
    if rx == 0 or ry == 0:
        path.lineTo(x2, y2)
        return
    dx, dy = (x1 - x2) / 2, (y1 - y2) / 2
    lam = (dx * dx) / (rx * rx) + (dy * dy) / (ry * ry)
    if lam > 1:
        rx, ry = rx * math.sqrt(lam), ry * math.sqrt(lam)
    sign = -1 if large == sweep else 1
    num = rx * rx * ry * ry - rx * rx * dy * dy - ry * ry * dx * dx
    coef = sign * math.sqrt(max(0, num / (rx * rx * dy * dy + ry * ry * dx * dx)))
    cx_, cy_ = coef * rx * dy / ry, -coef * ry * dx / rx
    cx, cy = cx_ + (x1 + x2) / 2, cy_ + (y1 + y2) / 2
    start = math.degrees(math.atan2((dy - cy_) / ry, (dx - cx_) / rx))
    end = math.degrees(math.atan2((-dy - cy_) / ry, (-dx - cx_) / rx))
    span = end - start
    if sweep and span < 0:
        span += 360
    elif not sweep and span > 0:
        span -= 360
    path.arcTo(QRectF(cx - rx, cy - ry, 2 * rx, 2 * ry), -start, -span)


def draw_icon(painter: QPainter, name: str, rect: QRectF, color: str, width: float = 1.8) -> None:
    painter.save()
    painter.setRenderHint(QPainter.RenderHint.Antialiasing)
    painter.translate(rect.topLeft())
    painter.scale(rect.width() / 24, rect.height() / 24)
    pen = QPen(QColor(color), width)
    pen.setCapStyle(Qt.PenCapStyle.RoundCap)
    pen.setJoinStyle(Qt.PenJoinStyle.RoundJoin)
    painter.setPen(pen)
    painter.setBrush(Qt.BrushStyle.NoBrush)
    if name == "grip":
        painter.setPen(Qt.PenStyle.NoPen)
        painter.setBrush(QColor(color))
        for cx in (9, 15):
            for cy in (6, 12, 18):
                painter.drawEllipse(QRectF(cx - 1.6, cy - 1.6, 3.2, 3.2))
    else:
        painter.drawPath(_parse_path(_ICON_PATHS[name]))
    painter.restore()


def icon(name: str, color: str = ACCENT, size: int = 20) -> QIcon:
    result = QIcon()
    for c, mode in ((color, QIcon.Mode.Normal), ("#FFFFFF", QIcon.Mode.Active), ("#FFFFFF", QIcon.Mode.Selected)):
        pm = QPixmap(size * 2, size * 2)
        pm.fill(Qt.GlobalColor.transparent)
        p = QPainter(pm)
        draw_icon(p, name, QRectF(0, 0, size * 2, size * 2), c)
        p.end()
        pm.setDevicePixelRatio(2)
        result.addPixmap(pm, mode)
    return result


def app_icon() -> QIcon:
    return QIcon(pixmap("token_baron.png", 256, ratio=1))


STYLESHEET = f"""
QWidget {{ color: {TEXT}; }}
QMainWindow, #Canvas {{ background: qlineargradient(x1:0, y1:0, x2:1, y2:1, stop:0 {GRADIENT_START}, stop:1 {CANVAS}); }}
QScrollArea, QScrollArea > QWidget > QWidget {{ background: transparent; border: none; }}
QToolTip {{ background: {DIALOG}; color: {TEXT}; border: 1px solid {POPUP_BORDER}; padding: 6px 8px; border-radius: 6px; }}
QLineEdit, QSpinBox, QComboBox {{ background: {FIELD}; border: 1px solid {ACTION_BORDER}; border-radius: 8px;
    padding: 8px 10px; color: {TEXT}; selection-background-color: {INTERACTION}; min-height: 22px; }}
QLineEdit:focus, QSpinBox:focus, QComboBox:focus {{ border: 1px solid {ACCENT}; }}
QLineEdit:disabled, QComboBox:disabled {{ color: {TEXT_TERTIARY}; }}
QComboBox::drop-down {{ border: none; width: 24px; }}
QComboBox QAbstractItemView {{ background: {DIALOG}; border: 1px solid {POPUP_BORDER}; selection-background-color: {INTERACTION};
    color: {TEXT}; outline: 0; }}
QScrollBar:vertical {{ background: transparent; width: 10px; margin: 2px; }}
QScrollBar::handle:vertical {{ background: #4A4C58; border-radius: 4px; min-height: 30px; }}
QScrollBar::handle:vertical:hover {{ background: #B197F2; }}
QScrollBar::add-line, QScrollBar::sub-line, QScrollBar::add-page, QScrollBar::sub-page {{ height: 0; width: 0; background: none; }}
QScrollBar:horizontal {{ height: 0; }}
QRadioButton, QCheckBox {{ spacing: 8px; color: {TEXT}; }}
QRadioButton::indicator {{ width: 16px; height: 16px; border-radius: 9px; border: 2px solid {TEXT_DETAIL}; }}
QRadioButton::indicator:checked {{ background: {SLIDER_FILL}; border: 2px solid {SLIDER_FILL}; }}
QMenu {{ background: {DIALOG}; border: 1px solid {POPUP_BORDER}; padding: 4px; }}
QMenu::item {{ padding: 6px 18px; border-radius: 4px; }}
QMenu::item:selected {{ background: {INTERACTION}; }}
QSlider::groove:horizontal {{ height: 4px; background: {SLIDER_TRACK}; border-radius: 2px; }}
QSlider::sub-page:horizontal {{ background: {SLIDER_FILL}; border-radius: 2px; }}
QSlider::handle:horizontal {{ background: {HIGHLIGHT}; width: 18px; height: 18px; margin: -7px 0; border-radius: 9px; }}
"""
