import os
from datetime import datetime, timedelta, timezone

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

import pytest

PyQt6 = pytest.importorskip("PyQt6.QtWidgets")
from PyQt6.QtCore import Qt
from PyQt6.QtTest import QTest
from PyQt6.QtWidgets import QApplication, QWidget

from token_scrooge.app.controller import AppController
from token_scrooge.app.panels import Dialog, PanelHost
from token_scrooge.app.settings_panel import (AccountSettingsPanel, GlobalSettingsPanel, MinuteSlider, QuietHoursRange,
                                              ReminderEditor)
from token_scrooge.core.model import ProviderAccount, QuotaSnapshot, QuotaWindow, UsageUnit
from token_scrooge.core.settings import RadarSettings, ReminderSeverity, TimedReminder
from token_scrooge.infra.store import LocalStore

NOW = datetime(2026, 11, 17, 12, 0, tzinfo=timezone.utc)


@pytest.fixture(scope="module")
def qapp():
    return QApplication.instance() or QApplication([])


@pytest.fixture
def store(tmp_path):
    return LocalStore(tmp_path / "radar.db", lambda: NOW)


def make_controller(store, tmp_path, demo=False):
    controller = AppController(store, None, tmp_path)
    controller.state.settings = store.read("settings", RadarSettings) or RadarSettings()
    if demo:
        controller.state.load_demo(NOW)
    else:
        saved = store.load_monitoring_state()
        controller.state.accounts = saved.accounts
        controller.state.snapshots = {s.AccountId: s for s in saved.snapshots}
    return controller


def seed_account(store):
    account = ProviderAccount("acct", "Claude", "Personal", "me@example.invalid", "org", PollingMinutes=5)
    window = QuotaWindow("weekly", "weekly limit", UsageUnit.Percent, 40, 100, NOW + timedelta(days=3), 10080)
    snapshot = QuotaSnapshot("acct", account.Subject, "org", NOW, (window,), None, PlanType="max")
    store.write_monitoring_state([account], [snapshot])
    return account


def saved_settings(store):
    return store.read("settings", RadarSettings)


def global_panel(controller, **kw):
    calls = {"history": 0, "test": 0, "login": []}
    panel = GlobalSettingsPanel(controller, lambda: calls.__setitem__("history", calls["history"] + 1),
                                lambda: calls.__setitem__("test", calls["test"] + 1),
                                kw.pop("launch", lambda on: calls["login"].append(on)), **kw)
    panel.resize(900, 760)
    return panel, calls


def test_global_toggles_and_combos_persist(qapp, store, tmp_path):
    controller = make_controller(store, tmp_path)
    panel, _ = global_panel(controller)
    panel.notifications.switch.click()
    assert saved_settings(store).Notifications is False
    panel.retention.setCurrentIndex(panel.retention.findData(30))
    panel.time_zone.setCurrentIndex(panel.time_zone.findData("Europe/Amsterdam"))
    panel.classic_chimes.setChecked(True)
    s = saved_settings(store)
    assert (s.HistoryRetentionDays, s.TimeZoneId, s.SoundTheme) == (30, "Europe/Amsterdam", "Classic")
    assert controller.settings == s
    assert panel.status_text() == ""


def test_debounced_sliders_flush_on_close(qapp, store, tmp_path):
    controller = make_controller(store, tmp_path)
    panel, _ = global_panel(controller)
    panel.volume.setValue(20)
    panel.info.slider.setValue(70)
    assert saved_settings(store) is None  # Still inside the debounce window.
    assert panel.before_close() is True
    s = saved_settings(store)
    assert (s.SoundVolume, s.InfoThreshold) == (20, 70)


def test_invalid_thresholds_block_save_and_close(qapp, store, tmp_path):
    controller = make_controller(store, tmp_path)
    panel, _ = global_panel(controller)
    panel.critical.slider.setValue(40)  # Above the 30% warning level.
    assert panel.before_close() is False
    assert "Critical" in panel.status_text()
    assert saved_settings(store) is None
    panel.critical.slider.setValue(5)
    assert panel.before_close() is True
    assert saved_settings(store).CriticalThreshold == 5


def test_quiet_hours_need_distinct_times(qapp, store, tmp_path):
    controller = make_controller(store, tmp_path)
    panel, _ = global_panel(controller)
    panel.quiet_range.set_hours(22, 22)
    panel.quiet_range.changed.emit()
    assert "quiet hours" in panel.status_text()
    panel.quiet_range.set_hours(22, 6)
    panel.quiet_range.changed.emit()
    s = saved_settings(store)
    assert (s.QuietStartHour, s.QuietEndHour) == (22, 6)
    assert panel.quiet_summary.text() == "10 PM – 6 AM · 8 hours · Overnight"


def test_save_failure_keeps_previous_settings(qapp, store, tmp_path, monkeypatch):
    controller = make_controller(store, tmp_path)
    panel, _ = global_panel(controller)

    def fail(*_a, **_k):
        raise OSError("disk full")
    monkeypatch.setattr(store, "write", fail)
    panel.sound.switch.click()
    assert controller.settings.Sound is True
    assert "could not be saved" in panel.status_text()
    assert panel.before_close() is False


def test_reminders_save_sorted(qapp, store, tmp_path):
    controller = make_controller(store, tmp_path)
    panel, _ = global_panel(controller)
    panel.refresh_reminders.add_reminder()
    assert panel.before_close() is True
    reminders = saved_settings(store).QuotaRefreshReminders
    assert len(reminders) == len(RadarSettings().effective_refresh_reminders) + 1
    assert [r.hours for r in reminders] == sorted((r.hours for r in reminders), reverse=True)


def test_reminder_editor_rejects_non_positive_amount(qapp):
    editor = ReminderEditor([TimedReminder("a", 2, "Hours", ReminderSeverity.Warning)])
    changes = []
    editor.changed.connect(lambda: changes.append(1))
    from PyQt6.QtWidgets import QLineEdit  # noqa: E402
    field = editor.findChild(QLineEdit)
    field.setText("0")
    field.textEdited.emit("0")
    assert editor.error.text() == "Enter a Positive Whole Number" and not changes
    field.setText("3")
    field.editingFinished.emit()
    assert editor.value[0].Amount == 3 and changes


def test_launch_at_login_failure_reverts(qapp, store, tmp_path):
    controller = make_controller(store, tmp_path)

    def broken(_on):
        raise OSError("no autostart dir")
    panel, _ = global_panel(controller, launch=broken)
    panel.startup.switch.click()
    assert saved_settings(store).LaunchAtSignIn is True
    assert panel.startup.is_checked() is True
    assert panel.status_text() == "Launch at Sign-In Could Not Be Changed"


def test_demo_changes_stay_in_memory(qapp, store, tmp_path):
    controller = make_controller(store, tmp_path, demo=True)
    panel, _ = global_panel(controller)
    assert not panel.startup.isEnabled()
    panel.notifications.switch.click()
    assert controller.settings.Notifications is False
    assert saved_settings(store) is None


def test_account_rename_polling_and_alerts_persist(qapp, store, tmp_path):
    seed_account(store)
    controller = make_controller(store, tmp_path)
    panel = AccountSettingsPanel(controller, "acct", lambda a: None, lambda a: None)
    assert panel.title_label.text().startswith("Claude / Personal · ")
    panel.name.setText("  Work  ")
    panel.name.editingFinished.emit()
    assert store.load_monitoring_state().accounts[0].Nickname == "Work"
    assert panel.title_label.text().startswith("Claude / Work")
    panel.polling.setValue(12)
    assert panel.before_close() is True
    assert store.load_monitoring_state().accounts[0].PollingMinutes == 12
    panel.exhausted.switch.click()
    rule = saved_settings(store).rule_for("acct")
    assert rule.Exhausted is False and rule.Restored is True
    panel.enabled.switch.click()
    assert store.load_monitoring_state().accounts[0].Enabled is False


def test_account_colors(qapp, store, tmp_path):
    seed_account(store)
    controller = make_controller(store, tmp_path)
    panel = AccountSettingsPanel(controller, "acct", lambda a: None, lambda a: None)
    assert panel.choose_color("#55C9BD")
    assert store.load_monitoring_state().accounts[0].LineColor == "#55C9BD"
    assert panel.add_custom_color("#123abc")
    assert saved_settings(store).CustomLineColors == ("#123ABC",)
    assert store.load_monitoring_state().accounts[0].LineColor == "#123ABC"
    selected = [w.hex for w in panel.swatches.items() if getattr(w, "selected", False)]
    assert selected == ["#123ABC"]


def test_remove_confirms_then_closes(qapp, store, tmp_path):
    seed_account(store)
    controller = make_controller(store, tmp_path)
    window = QWidget()
    window.resize(1100, 900)
    host = PanelHost(window)
    removed = []
    panel = AccountSettingsPanel(controller, "acct", lambda a: None,
                                 lambda a: (removed.append(a.Id), controller.remove_account(a.Id)))
    host.show_panel(panel)
    panel.remove_button.click()
    dialog = host.top()
    assert isinstance(dialog, Dialog)
    dialog._choose(lambda: panel.remove_now())
    assert removed == ["acct"]
    assert not host.is_open
    assert store.load_monitoring_state().accounts == []


def test_quiet_range_geometry_and_keys(qapp):
    w = QuietHoursRange(0, 8)
    w.resize(564, 140)
    assert w.handle_x(True) == pytest.approx(32 + 0.5 * 500)
    assert w.handle_x(False) == pytest.approx(32 + 20 / 24 * 500)
    assert QuietHoursRange.position(12, end=True) == 1.0 and QuietHoursRange.position(12) == 0.0
    w.setFocus()
    QTest.keyClick(w, Qt.Key.Key_Left)
    assert w.start_hour == 23
    QTest.keyClick(w, Qt.Key.Key_Space)
    QTest.keyClick(w, Qt.Key.Key_Right)
    assert w.end_hour == 9


def test_minute_slider_ticks_match_thumb(qapp):
    s = MinuteSlider(5)
    s.resize(318, 44)
    for minute in (1, 7, 15, 30):
        assert s.minute_at(s.anchor(minute)) == minute
    QTest.keyClick(s, Qt.Key.Key_End)
    assert s.value() == 30
