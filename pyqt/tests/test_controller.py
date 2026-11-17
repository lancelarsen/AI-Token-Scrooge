"""Refresh orchestration and account lifecycle against a fake monitor (no provider I/O)."""
import os
import threading
import time
from datetime import datetime, timedelta, timezone

os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")

import pytest
from PyQt6.QtWidgets import QApplication

from token_scrooge.app import tasks
from token_scrooge.app.controller import AppController
from token_scrooge.core.model import (ProviderAccount, QuotaSnapshot, QuotaWindow, SourceHealth, UsageUnit, replace)
from token_scrooge.infra.errors import ProviderAccessError
from token_scrooge.infra.store import LocalStore

app = QApplication.instance() or QApplication([])
tasks.init()


def now():
    return datetime.now(timezone.utc)


def snapshot(account, used=40.0):
    return QuotaSnapshot(account.Id, account.Subject, account.Scope, now(),
                         (QuotaWindow("codex/primary", "Codex · 5-Hour", UsageUnit.Percent, used, 100, now() + timedelta(hours=3)),),
                         None)


class FakeMonitor:
    def __init__(self):
        self.results = {}
        self.calls = []
        self.disconnected = []
        self.gate = None
        self.fail_disconnect = False

    def refresh(self, account, token):
        self.calls.append(account.Id)
        if self.gate is not None:
            self.gate.wait(5)
        result = self.results.get(account.Id)
        if isinstance(result, BaseException):
            raise result
        return result or snapshot(account)

    def disconnect(self, account, token):
        if self.fail_disconnect:
            raise OSError("keychain locked")
        self.disconnected.append(account.credential)


def pump(predicate, timeout=5.0):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        app.processEvents()
        if predicate():
            return True
        time.sleep(0.01)
    return False


@pytest.fixture
def setup(tmp_path):
    store = LocalStore(tmp_path / "radar.db")
    account = ProviderAccount("a1", "OpenAI", "Personal", "me@example.com", "acct", CredentialId="c1")
    store.write_monitoring_state([account], [])
    monitor = FakeMonitor()
    controller = AppController(store, monitor, tmp_path)
    return controller, store, monitor, account


def test_successful_refresh_commits_history_before_display(setup):
    controller, store, monitor, account = setup
    controller.start()
    assert pump(lambda: not controller.is_refreshing and controller.state.snapshots.get("a1") is not None
                and controller.state.snapshots["a1"].Health == SourceHealth.Healthy)
    saved = store.load_monitoring_state()
    assert saved.snapshots[0].Windows[0].Used == 40.0
    assert len(store.history("a1", now() - timedelta(hours=1), now() + timedelta(minutes=1))) == 1
    controller.shutdown()


def test_failure_keeps_last_values_and_backs_off(setup):
    controller, store, monitor, account = setup
    controller.start()
    assert pump(lambda: not controller.is_refreshing and "a1" in controller.state.snapshots
                and controller.state.snapshots["a1"].Health == SourceHealth.Healthy)
    retry = now() + timedelta(minutes=20)
    monitor.results["a1"] = ProviderAccessError("Slow down", SourceHealth.Stale, retry)
    controller.refresh(force=True)
    assert pump(lambda: not controller.is_refreshing)
    current = controller.state.snapshots["a1"]
    assert current.Health == SourceHealth.Stale and current.Windows[0].Used == 40.0
    calls = len(monitor.calls)
    controller.refresh(force=True)  # Manual refresh still honors the provider's retry deadline.
    app.processEvents()
    assert len(monitor.calls) == calls
    controller.shutdown()


def test_trigger_during_active_read_reuses_it(setup):
    controller, store, monitor, account = setup
    monitor.gate = threading.Event()
    controller.start()
    assert pump(lambda: monitor.calls == ["a1"])
    finished = []
    controller.refresh(force=True, done=lambda: finished.append(True))
    assert monitor.calls == ["a1"]
    monitor.gate.set()
    assert pump(lambda: finished == [True])
    assert monitor.calls == ["a1"]
    controller.shutdown()


def test_stale_result_from_removed_account_is_rejected(setup):
    controller, store, monitor, account = setup
    monitor.gate = threading.Event()
    controller.start()
    assert pump(lambda: controller.is_refreshing)
    controller.remove_account("a1")
    monitor.gate.set()
    assert pump(lambda: not controller.is_refreshing)
    assert "a1" not in controller.state.snapshots
    assert store.load_monitoring_state().snapshots == []
    controller.shutdown()


def test_reconnect_retires_previous_credential_after_commit(setup):
    controller, store, monitor, account = setup
    controller.start()
    assert pump(lambda: not controller.is_refreshing)
    reconnected = replace(account, CredentialId="c2")
    controller.accept_account(reconnected, snapshot(reconnected, 10))
    assert store.load_monitoring_state().accounts[0].CredentialId == "c2"
    assert pump(lambda: monitor.disconnected == ["c1"])
    assert pump(lambda: store.load_monitoring_state().retired == [])
    controller.shutdown()


def test_failed_cleanup_stays_queued(setup):
    controller, store, monitor, account = setup
    monitor.fail_disconnect = True
    controller.start()
    assert pump(lambda: not controller.is_refreshing)
    controller.remove_account("a1")
    assert pump(lambda: not controller._cleanup_running)
    saved = store.load_monitoring_state()
    assert saved.accounts == [] and [a.CredentialId for a in saved.retired] == ["c1"]
    monitor.fail_disconnect = False
    controller.refresh(force=False)
    assert pump(lambda: store.load_monitoring_state().retired == [])
    controller.shutdown()


def test_demo_mode_never_writes(setup):
    controller, store, monitor, account = setup
    controller.start(demo=True)
    controller.set_history_enabled(False)
    assert controller.settings.HistoryEnabled is False
    assert store.read("settings", type(controller.settings)) is None
    assert monitor.calls == []
    controller.shutdown()
