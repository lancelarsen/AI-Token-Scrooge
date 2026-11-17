from datetime import date, datetime, timedelta, timezone
from zoneinfo import ZoneInfo

import pytest

from token_scrooge.app.presentation import (DashboardState, HorizonViewport, build_history, color_for_account,
                                            countdown_target, downsample)
from token_scrooge.core.alerts import AlertEvent
from token_scrooge.core.model import (CredentialProfileLocation, ProviderAccount, QuotaSnapshot, QuotaWindow, SourceHealth,
                                      UsageUnit, replace)
from token_scrooge.core.settings import AccountSortMode, RadarSettings
from token_scrooge.infra.errors import CancelToken, ProviderAccessError
from token_scrooge.infra.github import GitHubAppTokens, GitHubCredentialManager
from token_scrooge.infra.secrets import MemorySecretStore
from token_scrooge.infra.store import LocalStore

CENTRAL = ZoneInfo("America/Chicago")
NOW = datetime(2026, 10, 29, 9, 49, tzinfo=timezone(timedelta(hours=-5)))


def close(expected, actual, seconds=1.0):
    assert abs((expected - actual).total_seconds()) <= seconds, (expected, actual)


def test_opens_at_today_and_pans_back_seven_days():
    view = HorizonViewport(NOW, NOW + timedelta(days=4), CENTRAL, 980)
    assert view.following_today
    close(datetime(2026, 10, 29, tzinfo=CENTRAL), view.visible_start)
    assert view.visible_days == pytest.approx(7, abs=1e-6)
    view.scroll_to(0)
    assert not view.following_today
    close(NOW - timedelta(days=7), view.visible_start)
    close(NOW, view.visible_end)


def test_short_future_keeps_today_at_front():
    view = HorizonViewport(NOW, NOW + timedelta(hours=2), CENTRAL, 980)
    close(datetime(2026, 10, 29, tzinfo=CENTRAL), view.visible_start)
    assert view.end_at > view.last_event_at
    assert view.scroll_offset == pytest.approx(view.max_scroll)


def test_far_future_and_navigator_clamps():
    final = NOW + timedelta(days=45)
    view = HorizonViewport(NOW, final, CENTRAL, 980)
    view.center_from_overview(-500, 980)
    assert view.scroll_offset == 0
    view.center_from_overview(2000, 980)
    assert view.scroll_offset == pytest.approx(view.max_scroll)
    assert view.visible_end >= final
    left, width = view.overview(980)
    assert left + width == pytest.approx(980)


def test_overview_drag_uses_same_mapping():
    view = HorizonViewport(NOW, NOW + timedelta(days=35), CENTRAL, 980)
    start = view.scroll_offset
    at = NOW + timedelta(days=4)
    assert view.overview_position(at, 620) == pytest.approx(view.position_at(at) / view.content_width * 620)
    view.drag_overview(start, -40, 620)
    assert view.scroll_offset == pytest.approx(start - 40 / 620 * view.content_width)
    assert not view.following_today


def test_refresh_follows_today_until_user_browses():
    view = HorizonViewport(NOW, NOW + timedelta(days=12), CENTRAL, 980)
    view.scroll_to(view.position_at(NOW - timedelta(days=3)))
    browsing = view.visible_start
    view.update(NOW + timedelta(hours=3), NOW + timedelta(days=20), CENTRAL, 980)
    close(browsing, view.visible_start)
    view.follow_today()
    close(datetime(2026, 10, 29, tzinfo=CENTRAL), view.visible_start)


def test_history_off_starts_at_today():
    view = HorizonViewport(NOW, NOW + timedelta(days=3), CENTRAL, 980, history=False)
    assert view.scroll_offset == 0
    close(datetime(2026, 10, 29, tzinfo=CENTRAL), view.visible_start)


@pytest.mark.parametrize("start", [datetime(2026, 3, 5, 12, tzinfo=CENTRAL), datetime(2026, 10, 29, 12, tzinfo=CENTRAL)])
def test_consecutive_days_are_equally_spaced_across_dst(start):
    """Spring-forward (23h) and fall-back (25h) days must still be exactly one day wide."""
    view = HorizonViewport(start, start + timedelta(days=12), CENTRAL, 980)
    centers = [view.day_center(view.today + timedelta(days=i)) for i in range(-6, 12)]
    gaps = {round(b - a, 6) for a, b in zip(centers, centers[1:])}
    assert gaps == {round(view.pixels_per_day, 6)}
    for i in range(-6, 12):
        d = view.today + timedelta(days=i)
        midnight = datetime(d.year, d.month, d.day, tzinfo=CENTRAL)
        assert view.position_at(midnight) == pytest.approx(view.position_of_date(d))


def test_day_center_is_bucket_middle_not_time_of_day():
    view = HorizonViewport(NOW, NOW + timedelta(days=3), CENTRAL, 980)
    noon = datetime(2026, 10, 30, 12, tzinfo=CENTRAL)
    assert view.day_center(date(2026, 10, 30)) == pytest.approx(view.position_at(noon))


def test_demo_dashboard_sorting_and_countdown_expiry_slot():
    state = DashboardState()
    state.load_demo(NOW)
    state.settings = RadarSettings(AccountSort=AccountSortMode.LeastRemaining)
    assert state.ordered_accounts()[0].Id == "demo-claude"  # 6% left on its weekly limit
    state.settings = RadarSettings(AccountSort=AccountSortMode.NextReset)
    assert state.ordered_accounts()[0].Id == "demo-personal"
    personal = state.account("demo-personal")
    weekly = [r for r in state.rows_for(personal) if r.Window.DurationMinutes == 10080][0]
    assert countdown_target(weekly, NOW) == (NOW + timedelta(days=2), False)
    events = state.events(NOW)
    assert sum(e.IsExpiry for e in events) == 2


def test_stable_account_color():
    assert color_for_account("abc") == color_for_account("abc")
    assert color_for_account("abc") != color_for_account("abd")


def test_history_marks_replenishment_and_gaps():
    account = ProviderAccount("a", "OpenAI", "P", "s", "w")
    w = QuotaWindow("codex/secondary", "Codex · Weekly", UsageUnit.Percent, 50, 100, NOW + timedelta(days=2), 10080)
    snaps = [QuotaSnapshot("a", "s", "w", NOW - timedelta(minutes=m), (replace(w, Used=u),), None)
             for m, u in ((600, 40), (590, 60), (580, 10), (60, 20))]
    series = build_history(account, snaps, NOW - timedelta(days=7), NOW)
    assert [p.RemainingPercent for p in series.Points] == [60, 40, 90, 80]
    assert series.Points[2].ReplenishedFrom == 40
    assert series.Points[3].InterpolatedFromPrevious
    assert len(downsample(series.Points, lambda t: t.timestamp() / 3600, -1e12, 1e12)) == 4


def test_store_round_trip_history_runs_and_journal(tmp_path):
    clock = lambda: NOW
    store = LocalStore(tmp_path / "radar.db", clock)
    cid = "0" * 31 + "1"
    account = ProviderAccount("acct", "Claude", "P", "me@example.invalid", "org", CredentialId=cid,
                              ProfileLocation=CredentialProfileLocation.LocalSecurityDirectory)
    store.stage_credential("Claude", cid)
    assert [a.credential for a in store.load_monitoring_state().retired] == [cid]  # abandoned sign-in retries cleanup
    w = QuotaWindow("weekly", "Weekly limit", UsageUnit.Percent, 10, 100, NOW + timedelta(days=3), 10080)
    for minutes, used in ((30, 10), (20, 10), (10, 10), (0, 25)):
        snap = QuotaSnapshot("acct", account.Subject, "org", NOW - timedelta(minutes=minutes), (replace(w, Used=used),), None)
        alert = AlertEvent(f"k{minutes}", "acct", "weekly", "t", "d", NOW)
        store.write_monitoring_state([account], [snap], alerts=[alert], history_snapshot=snap)
    state = store.load_monitoring_state()
    assert state.accounts == [account] and state.retired == []  # acceptance cleared the journal
    assert len(state.alerts) == 4
    history = store.history("acct", NOW - timedelta(days=1), NOW)
    # One run of identical values (first + last endpoint), then the change.
    assert [h.Windows[0].Used for h in history] == [10, 10, 25]
    assert history[1].HistoryContinuousFrom == NOW - timedelta(minutes=30)
    store.record_alert_delivery("k0", True)
    assert [a.Delivered for a in store.load_monitoring_state().alerts if a.Key == "k0"] == [True]
    with pytest.raises(RuntimeError):
        store.stage_credential("Claude", cid)  # cannot reuse an active credential


def test_store_rejects_history_for_unaccepted_account(tmp_path):
    store = LocalStore(tmp_path / "radar.db")
    snap = QuotaSnapshot("ghost", "s", "w", NOW, (), None)
    with pytest.raises(ValueError):
        store.write_monitoring_state([], [snap], history_snapshot=snap)


class FakeUsage:
    def identify(self, token):
        return "42", "octo"


class FakeFlow:
    def __init__(self):
        self.calls = 0

    def refresh(self, previous):
        self.calls += 1
        return replace(previous, AccessToken="ghu_new", RefreshToken="ghr_new",
                       ExpiresAt=NOW + timedelta(hours=8), RefreshExpiresAt=NOW + timedelta(days=180))


def test_github_renewal_persists_and_verifies():
    secrets = MemorySecretStore()
    flow = FakeFlow()
    manager = GitHubCredentialManager(secrets, FakeUsage(), flow, lambda: NOW)
    cid = "a" * 32
    manager.save_app(cid, "42", GitHubAppTokens("Iv1.abc", "ghu_old", "ghr_old", NOW + timedelta(minutes=1), NOW + timedelta(days=30)))
    account = ProviderAccount("g", "GitHub Copilot", "W", "42", "personal", CredentialId=cid)
    assert manager.read_access_token(account, CancelToken()) == "ghu_new"
    assert flow.calls == 1 and "ghu_new" in secrets.values[cid] and "RenewalStarted\":false" in secrets.values[cid]
    assert manager.read_access_token(account, CancelToken()) == "ghu_new" and flow.calls == 1
    assert "ghu_new" not in repr(GitHubAppTokens("c", "ghu_new", "r", NOW, NOW))


def test_github_interrupted_renewal_requires_reconnect():
    secrets = MemorySecretStore()
    manager = GitHubCredentialManager(secrets, FakeUsage(), FakeFlow(), lambda: NOW)
    cid = "b" * 32
    manager.save_app(cid, "42", GitHubAppTokens("Iv1.abc", "ghu_old", "ghr_old", NOW + timedelta(minutes=1), NOW + timedelta(days=30)))
    secrets.values[cid] = secrets.values[cid].replace('"RenewalStarted":false', '"RenewalStarted":true')
    with pytest.raises(ProviderAccessError):
        manager.read_access_token(ProviderAccount("g", "GitHub Copilot", "W", "42", "personal", CredentialId=cid), CancelToken())
