"""Account cards and quota rows (port of AccountQuotaView, QuotaRowView, AccountSortControl, Ordering).

One card per account; each quota window has its own bar, alarm and countdown. Timestamp-only
updates touch countdown labels instead of rebuilding, so polling never interrupts a drag."""
from __future__ import annotations

import json
from datetime import datetime, timezone
from typing import Callable, Optional

from PyQt6.QtCore import QMimeData, QPoint, QRectF, Qt, pyqtSignal
from PyQt6.QtGui import QColor, QDrag, QPainter, QPen
from PyQt6.QtWidgets import QGridLayout, QHBoxLayout, QLabel, QSizePolicy, QVBoxLayout, QWidget

from ..core import serialization as ser
from ..core.labels import format_reset, plan_label
from ..core.settings import AccountSortMode, AlertThresholds, ReminderSeverity, TimedReminder
from ..core.model import ProviderAccount
from . import theme as T
from .presentation import QuotaRow, countdown_label, countdown_target
from .widgets import (ActionButton, AlarmToggle, Card, PlanBadge, SegmentedControl, UsageBar, label, set_label_color,
                      severity_key)

SORT_OPTIONS = [(AccountSortMode.CustomOrder, "Custom Order", "grip"), (AccountSortMode.MostRemaining, "Most Remaining", "most"),
                (AccountSortMode.LeastRemaining, "Least Remaining", "least"), (AccountSortMode.NextReset, "Next Reset", "clock")]
MIME = "application/x-scrooge-account"


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


class Glyph(QWidget):
    def __init__(self, name: str, color: str, size: int = 25, parent=None):
        super().__init__(parent)
        self.name, self.color = name, color
        self.setFixedSize(size, size)

    def set(self, name: str, color: str) -> None:
        self.name, self.color = name, color
        self.update()

    def paintEvent(self, e):
        T.draw_icon(QPainter(self), self.name, QRectF(self.rect()).adjusted(1, 1, -1, -1), self.color, 2.0)


class Grip(QWidget):
    """Custom Order handle (six-dot grip). Dragging starts an account move."""

    def __init__(self, account_id: str, description: str, parent=None):
        super().__init__(parent)
        self.account_id = account_id
        self.setFixedWidth(28)
        self.setCursor(Qt.CursorShape.OpenHandCursor)
        self.setToolTip("Drag to Reorder")
        self.setAccessibleName(f"Reorder {description}")
        self._press: Optional[QPoint] = None

    def mousePressEvent(self, e):
        self._press = e.position().toPoint()

    def mouseMoveEvent(self, e):
        if self._press is None or (e.position().toPoint() - self._press).manhattanLength() < 6:
            return
        drag = QDrag(self)
        mime = QMimeData()
        mime.setData(MIME, self.account_id.encode())
        drag.setMimeData(mime)
        card = self.parentWidget()
        while card is not None and not isinstance(card, AccountCard):
            card = card.parentWidget()
        if card is not None:
            pm = card.grab()
            drag.setPixmap(pm.scaledToWidth(min(600, pm.width()), Qt.TransformationMode.SmoothTransformation))
            drag.setHotSpot(QPoint(20, 20))
        self._press = None
        drag.exec(Qt.DropAction.MoveAction)

    def mouseReleaseEvent(self, e):
        self._press = None

    def paintEvent(self, e):
        p = QPainter(self)
        T.draw_icon(p, "grip", QRectF(2, self.height() / 2 - 12, 24, 24), T.ACCENT)
        p.setPen(QPen(QColor(T.CARD_BORDER), 1))
        p.drawLine(self.width() - 1, 8, self.width() - 1, self.height() - 8)


class QuotaRowWidget(QWidget):
    def __init__(self, row: QuotaRow, controller, open_details: Callable[[QuotaRow], None],
                 open_reserve: Callable[[str], None], interaction: Callable[[bool], None], on_error: Callable[[str, str], None]):
        super().__init__()
        self.row, self.controller = row, controller
        self._open_reserve = open_reserve
        self._deadline: tuple[Optional[datetime], bool] = (None, False)
        settings = controller.settings
        rule = settings.rule_for(row.Account.Id, row.Window.Id)
        enabled = settings.capacity_alerts_enabled(row.Account.Id, row.Window.Id)
        description = f"{row.Account.Provider} / {row.account_label} · {row.window_title}"

        self.quota = QWidget()
        q = QVBoxLayout(self.quota)
        q.setContentsMargins(0, 0, 0, 0)
        q.setSpacing(4)
        title = label(row.window_title, 14, T.TEXT_DETAIL, wrap=True)
        q.addWidget(title)
        if row.card_note:
            note = label(row.card_note, 11, T.TEXT_TERTIARY, wrap=True)
            q.addWidget(note)
        self.quota.setCursor(Qt.CursorShape.PointingHandCursor)
        self.quota.mouseReleaseEvent = lambda e: open_details(row)

        self.alarm = AlarmToggle(enabled, f"Row Alerts · {description}")
        self.bar = UsageBar(row.Window.fill, row.used_text, row.left_text, severity_key(row.capacity),
                            AlertThresholds(rule.LowThreshold, rule.CriticalThreshold, rule.InfoThreshold), enabled, description)
        self.bar.clicked.connect(lambda: open_details(row))
        self.bar.interaction.connect(interaction)

        def save_thresholds(values: AlertThresholds):
            try:
                controller.save_thresholds(row.Account.Id, row.Window.Id, values)
            except Exception:  # noqa: BLE001
                on_error("Alert Level Not Saved", "Scrooge could not save this alert level. Check local folder access and try again.")
        self.bar.thresholds_changed.connect(save_thresholds)

        def save_alarm(on: bool):
            try:
                controller.set_capacity_alerts(row.Account.Id, row.Window.Id, on)
            except Exception:  # noqa: BLE001
                self.alarm.set_enabled_state(not on)
                on_error("Alert Choice Not Saved", "Scrooge could not save this alert choice. Check local folder access and try again.")
        self.alarm.changed.connect(save_alarm)

        self.usage = QWidget()
        u = QHBoxLayout(self.usage)
        u.setContentsMargins(0, 0, 0, 0)
        u.setSpacing(8)
        u.addWidget(self.alarm)
        u.addWidget(self.bar, 1)

        self.reset = QWidget()
        r = QHBoxLayout(self.reset)
        r.setContentsMargins(0, 0, 0, 0)
        r.setSpacing(12)
        self.reset_icon = Glyph("refresh", T.ACCENT)
        r.addWidget(self.reset_icon, 0, Qt.AlignmentFlag.AlignTop)
        texts = QVBoxLayout()
        texts.setSpacing(3)
        self.countdown = label("", 20, T.COUNTDOWN, True)
        self.exact = label("", 12, T.TEXT_DETAIL)
        texts.addWidget(self.countdown)
        texts.addWidget(self.exact)
        r.addLayout(texts)
        r.addStretch(1)
        self.reset.setCursor(Qt.CursorShape.PointingHandCursor)
        self.reset.mouseReleaseEvent = lambda e: (open_reserve(row.Account.Id) if self._deadline[1] else open_details(row))

        self.setMinimumHeight(56)
        self.grid = QGridLayout(self)
        self.grid.setContentsMargins(0, 0, 0, 0)
        self.grid.setHorizontalSpacing(12)
        self.grid.setVerticalSpacing(8)
        self._wide: Optional[bool] = None
        self.set_wide(False)
        self.update_clock(utcnow())

    def set_wide(self, wide: bool) -> None:
        if self._wide == wide:
            return
        self._wide = wide
        for w in (self.quota, self.usage, self.reset):
            self.grid.removeWidget(w)
        for c in range(3):
            self.grid.setColumnStretch(c, 0)
            self.grid.setColumnMinimumWidth(c, 0)
        if wide:
            self.quota.setFixedWidth(156)
            self.reset.setFixedWidth(200)
            self.grid.addWidget(self.quota, 0, 0)
            self.grid.addWidget(self.usage, 0, 1)
            self.grid.addWidget(self.reset, 0, 2)
            self.grid.setColumnStretch(1, 1)
        else:
            self.quota.setMinimumWidth(0)
            self.quota.setMaximumWidth(16777215)
            self.reset.setMinimumWidth(0)
            self.reset.setMaximumWidth(16777215)
            self.grid.addWidget(self.quota, 0, 0)
            self.grid.addWidget(self.reset, 0, 1)
            self.grid.addWidget(self.usage, 1, 0, 1, 2)
            self.grid.setColumnStretch(0, 1)
            self.grid.setColumnStretch(1, 1)

    def update_clock(self, now: datetime) -> None:
        at, is_expiry = countdown_target(self.row, now)
        self.reset.setVisible(at is not None or bool(self._wide))
        self._deadline = (at, is_expiry)
        if at is None:
            self.countdown.setText("")
            self.exact.setText("")
            self.reset_icon.hide()
            return
        self.reset_icon.show()
        kind = "Earned Reset Expires" if is_expiry else "Quota Refresh"
        self.reset.setToolTip(kind)
        self.reset.setAccessibleName(f"{kind}, {format_reset(at, self.controller.zone)}")
        self.exact.setText(format_reset(at, self.controller.zone))
        self.countdown.setText(countdown_label(at, now))
        set_label_color(self.countdown, T.EXPIRY if is_expiry else T.COUNTDOWN)
        settings = self.controller.settings
        severity = None
        if settings.capacity_alerts_enabled(self.row.Account.Id, self.row.Window.Id):
            due = TimedReminder.due(settings.effective_refresh_reminders, at, now)
            severity = due.Severity if due else None
        color = T.EXPIRY if is_expiry else {ReminderSeverity.Critical: T.CRITICAL, ReminderSeverity.Warning: T.WARNING,
                                            ReminderSeverity.Info: T.INFO}.get(severity, T.ACCENT)
        self.reset_icon.set("clock" if is_expiry else "refresh", color)


class AccountCard(Card):
    def __init__(self, account: ProviderAccount, rows: list[QuotaRow], controller, callbacks: dict, custom_order: bool):
        super().__init__(13)
        self.account = account
        self.rows: list[QuotaRowWidget] = []
        snapshot = controller.state.snapshots.get(account.Id)
        plan = plan_label(account.Provider, snapshot.PlanType if snapshot else None)
        description = f"{account.Provider} / {account.Nickname} · {plan}"
        outer = QHBoxLayout(self)
        outer.setContentsMargins(8 if custom_order else 20, 8, 12, 8)
        outer.setSpacing(12)
        if custom_order:
            outer.addWidget(Grip(account.Id, description))

        self.identity = QWidget()
        identity = QHBoxLayout(self.identity)
        identity.setContentsMargins(0, 0, 0, 0)
        identity.setSpacing(16)
        logo = QLabel()
        logo.setPixmap(T.provider_pixmap(account.Provider, 44))
        logo.setFixedSize(44, 44)
        identity.addWidget(logo)
        names = QVBoxLayout()
        names.setSpacing(5)
        names.addWidget(label(account.Provider, 20, T.TEXT, True))
        badge_row = QHBoxLayout()
        badge_row.addWidget(PlanBadge(f"{account.Nickname} · {plan}"))
        badge_row.addStretch(1)
        names.addLayout(badge_row)
        identity.addLayout(names)
        identity.addStretch(1)
        self.identity.setCursor(Qt.CursorShape.PointingHandCursor)
        first_window = rows[0].Window.Id if rows else None
        self.identity.mouseReleaseEvent = lambda e: callbacks["details"](account.Id, first_window)

        self.content = QWidget()
        self.grid = QGridLayout(self.content)
        self.grid.setContentsMargins(0, 0, 0, 0)
        self.grid.setSpacing(12)
        self.windows = QWidget()
        wl = QVBoxLayout(self.windows)
        wl.setContentsMargins(0, 4, 0, 4)
        wl.setSpacing(12)
        if rows:
            for row in rows:
                widget = QuotaRowWidget(row, controller, lambda r: callbacks["details"](r.Account.Id, r.Window.Id),
                                        callbacks["reserve"], callbacks["interaction"], callbacks["error"])
                self.rows.append(widget)
                wl.addWidget(widget)
        else:
            status = ("Monitoring Disabled" if not account.Enabled else
                      snapshot.Message if snapshot and snapshot.Message else "Waiting for Usage Data")
            manage = ActionButton(f"{status} · Manage Account", height=56)
            manage.clicked.connect(lambda: callbacks["account"](account.Id))
            wl.addWidget(manage)
        outer.addWidget(self.content, 1)

        gear = ActionButton(f"Settings for {description}", "settings", icon_only=True)
        gear.clicked.connect(lambda: callbacks["account"](account.Id))
        outer.addWidget(gear, 0, Qt.AlignmentFlag.AlignTop)
        self._wide: Optional[bool] = None
        self._reflow()

    def _reflow(self) -> None:
        wide = self.width() > 1050
        if wide == self._wide:
            return
        self._wide = wide
        self.grid.removeWidget(self.identity)
        self.grid.removeWidget(self.windows)
        if wide:
            self.identity.setFixedWidth(220)
            self.grid.addWidget(self.identity, 0, 0, Qt.AlignmentFlag.AlignVCenter)
            self.grid.addWidget(self.windows, 0, 1)
            self.grid.setColumnStretch(1, 1)
        else:
            self.identity.setMinimumWidth(0)
            self.identity.setMaximumWidth(16777215)
            self.grid.addWidget(self.identity, 0, 0)
            self.grid.addWidget(self.windows, 1, 0)
            self.grid.setColumnStretch(1, 0)
        for row in self.rows:
            row.set_wide(wide)

    def resizeEvent(self, e):
        super().resizeEvent(e)
        self._reflow()

    def update_clock(self, now: datetime) -> None:
        for row in self.rows:
            row.update_clock(now)


class AccountsView(QWidget):
    """Toolbar (sort + Add Account) and account cards."""

    def __init__(self, controller, callbacks: dict):
        super().__init__()
        self.controller = controller
        self.callbacks = dict(callbacks)
        self.callbacks["interaction"] = self._threshold_interaction
        self.cards: dict[str, AccountCard] = {}
        self._signature: Optional[str] = None
        self._editing = False
        self._pending = False
        self._drop_index: Optional[int] = None
        self.setAcceptDrops(True)
        self.layout_ = QVBoxLayout(self)
        self.layout_.setContentsMargins(0, 0, 0, 0)
        self.layout_.setSpacing(20)
        toolbar = QHBoxLayout()
        toolbar.setSpacing(12)
        self.sort = SegmentedControl(SORT_OPTIONS, controller.settings.AccountSort)
        self.sort.selected.connect(self._sort_changed)
        toolbar.addWidget(self.sort, 1)
        self.add = ActionButton("Add Account", "plus")
        self.add.clicked.connect(self._add_clicked)
        toolbar.addWidget(self.add)
        self.layout_.addLayout(toolbar)
        self.body = QVBoxLayout()
        self.body.setSpacing(20)
        self.layout_.addLayout(self.body)

    def _sort_changed(self, mode):
        try:
            self.controller.set_sort(mode)
        except Exception:  # noqa: BLE001
            self.callbacks["error"]("Sort Not Saved", "Scrooge could not save the sort order. Check local folder access and try again.")

    def _add_clicked(self):
        if self.controller.state.is_demo:
            self.callbacks["exit_demo"]()
        else:
            self.callbacks["add"]()

    def _threshold_interaction(self, active: bool) -> None:
        self._editing = active
        if not active and self._pending:
            self.render()

    def _compute_signature(self) -> str:
        c = self.controller
        snaps = {k: ser.to_plain(v) for k, v in c.state.snapshots.items()}
        for v in snaps.values():
            v.pop("ObservedAt", None)
            v.pop("ProviderDataAt", None)
        return json.dumps([ser.to_plain(c.state.accounts), snaps, ser.to_plain(c.settings), c.state.is_demo,
                           str(c.zone)], sort_keys=True, default=str)

    def render(self, force: bool = False) -> None:
        if self._editing:
            self._pending = True
            return
        self._pending = False
        signature = self._compute_signature()
        if signature == self._signature and not force:
            self.update_clock()
            return
        self._signature = signature
        c = self.controller
        self.sort.set_current(c.settings.AccountSort)
        demo = c.state.is_demo
        self.add.setText("Exit Demo" if demo else "Add Account")
        self.add._icon = "exit" if demo else "plus"
        self.add.updateGeometry()
        while self.body.count():
            item = self.body.takeAt(0)
            if item.widget():
                item.widget().deleteLater()
        self.cards.clear()
        if not c.state.accounts:
            empty = QWidget()
            e = QVBoxLayout(empty)
            e.setContentsMargins(24, 36, 24, 36)
            e.setSpacing(15)
            e.addWidget(label("Your AI capacity, in one quiet place.", 24, T.TEXT, True))
            e.addWidget(label("Connect an OpenAI Codex, GitHub Copilot, Claude, or Devin account to see its usage and upcoming resets.",
                              14, T.TEXT_SECONDARY, wrap=True))
            demo_button = ActionButton("Explore with Demo Data", "demo")
            demo_button.clicked.connect(self.callbacks["demo"])
            row = QHBoxLayout()
            row.addWidget(demo_button)
            row.addStretch(1)
            e.addLayout(row)
            self.body.addWidget(empty)
            return
        custom = c.settings.AccountSort == AccountSortMode.CustomOrder
        for account in c.state.ordered_accounts():
            card = AccountCard(account, c.state.rows_for(account), c, self.callbacks, custom)
            self.cards[account.Id] = card
            self.body.addWidget(card)

    def update_clock(self) -> None:
        now = utcnow()
        for card in self.cards.values():
            card.update_clock(now)

    def highlight(self, account_id: Optional[str]) -> Optional[AccountCard]:
        for key, card in self.cards.items():
            card.set_highlight(key == account_id)
        return self.cards.get(account_id) if account_id else None

    # ------------------------------------------------------------------ drag reorder
    def _target_index(self, y: float) -> int:
        ordered = list(self.cards.values())
        for i, card in enumerate(ordered):
            if y < card.geometry().center().y():
                return i
        return len(ordered)

    def dragEnterEvent(self, e):
        if e.mimeData().hasFormat(MIME):
            e.acceptProposedAction()

    def dragMoveEvent(self, e):
        if e.mimeData().hasFormat(MIME):
            self._drop_index = self._target_index(e.position().y())
            self.update()
            e.acceptProposedAction()

    def dragLeaveEvent(self, e):
        self._drop_index = None
        self.update()

    def dropEvent(self, e):
        account_id = bytes(e.mimeData().data(MIME)).decode()
        target = self._target_index(e.position().y())
        ids = list(self.cards.keys())
        self._drop_index = None
        if account_id in ids:
            current = ids.index(account_id)
            if target > current:
                target -= 1
            if target != current:
                try:
                    self.controller.move_account(account_id, target)
                except Exception:  # noqa: BLE001
                    self.callbacks["error"]("Order Not Saved", "Scrooge could not save the account order. Check local folder access and try again.")
        e.acceptProposedAction()
        self.update()

    def paintEvent(self, e):
        if self._drop_index is None or not self.cards:
            return
        cards = list(self.cards.values())
        if self._drop_index < len(cards):
            y = cards[self._drop_index].geometry().top() - 10
        else:
            y = cards[-1].geometry().bottom() + 10
        p = QPainter(self)
        p.setPen(QPen(QColor(T.ACCENT), 3))
        p.drawLine(8, y, self.width() - 8, y)
