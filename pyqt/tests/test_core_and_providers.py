"""Ports of the C# fixture tests for domain rules, provider mapping and parsers."""
from datetime import datetime, timedelta, timezone
from zoneinfo import ZoneInfo

import pytest

from token_scrooge.core.alerts import AlertEvaluator, MonitorStatus, next_refresh_after_success
from token_scrooge.core.model import (AllowanceProvenance, CapacityState, ProviderAccount, QuotaSnapshot, QuotaWindow,
                                      ReconnectError, SourceHealth, UsageUnit, accept_reconnect, replace)
from token_scrooge.core.settings import AlertThresholds, RadarSettings, TimedReminder, move_in_order, normalize_order
from token_scrooge.core import serialization as ser
from token_scrooge.infra import claude, devin
from token_scrooge.infra.codex import map_codex, valid_auth_url
from token_scrooge.infra.errors import ProviderAccessError, UserFacingError
from token_scrooge.infra.github import github_allowance, map_github, map_managed_quota

NOW = datetime(2026, 10, 25, 14, 46, tzinfo=timezone.utc)
ACCOUNT = ProviderAccount("a", "OpenAI", "Personal", "sample@example.invalid", "workspace-a")


def test_codex_prefers_multi_bucket_and_preserves_count_only_reserve():
    result = map_codex(ACCOUNT, {"accountId": "workspace-a", "rateLimits": {"primary": {"usedPercent": 99}},
                                 "rateLimitsByLimitId": {"codex": {"primary": {"usedPercent": 38, "windowDurationMins": 300, "resetsAt": 1792938000},
                                                                   "secondary": {"usedPercent": 74, "windowDurationMins": 10080}}},
                                 "rateLimitResetCredits": {"availableCount": 2, "credits": None}}, NOW)
    assert [w.remaining_percent for w in result.Windows] == [62, 26]
    assert result.Windows[0].Name == "Codex · 5-hour" and result.Windows[1].Name == "Codex · Weekly"
    assert result.Reserve.AvailableCount == 2 and result.Reserve.Credits is None
    assert result.Windows[0].ResetsAt == datetime.fromtimestamp(1792938000, timezone.utc)


def test_legacy_codex_and_absent_reserve():
    result = map_codex(ACCOUNT, {"rateLimits": {"primary": {"usedPercent": 10}}}, NOW)
    assert len(result.Windows) == 1 and result.Windows[0].ResetsAt is None and result.Reserve is None


def test_codex_rejects_different_workspace():
    with pytest.raises(UserFacingError):
        map_codex(ACCOUNT, {"accountId": "workspace-b"}, NOW)


def test_codex_duplicate_null_expiry_details_do_not_invent_reserve():
    result = map_codex(ACCOUNT, {"rateLimitsByLimitId": {"future_bucket": None}, "rateLimitResetCredits": {
        "availableCount": 3, "credits": [{"id": "x", "status": "available", "expiresAt": None},
                                         {"id": "x", "status": "available", "expiresAt": None}]}}, NOW)
    assert result.Windows == () and result.Reserve.AvailableCount == 3 and len(result.Reserve.Credits) == 1
    assert result.Reserve.has_partial_details and result.Reserve.earliest_expiry(NOW) is None
    assert result.Health == SourceHealth.Healthy


def test_codex_paused_usage():
    result = map_codex(ACCOUNT, {"accountId": "workspace-a", "ordinaryUsageAllowed": False,
                                 "rateLimits": {"primary": {"usedPercent": 100}}}, NOW)
    assert result.UsageAllowed is False and "paused" in result.Message


def test_codex_auth_url_allowlist():
    assert valid_auth_url("https://auth.openai.com/oauth/authorize?x=1")
    assert not valid_auth_url("http://auth.openai.com/")
    assert not valid_auth_url("https://auth.openai.com.evil.invalid/")


def test_copilot_missing_allowance_does_not_invent_quota():
    account = replace(ACCOUNT, Provider="GitHub Copilot", Scope="personal", Subject="42")
    result = map_github(account, {"usageItems": [{"unitType": "ai-credits", "grossQuantity": 8200}]}, NOW)
    assert result.Windows[0].Used == 8200 and result.Windows[0].remaining is None
    assert result.Windows[0].ResetsAt == datetime(2026, 11, 1, tzinfo=timezone.utc)


def test_copilot_sums_selected_unit_only():
    account = replace(ACCOUNT, Provider="GitHub Copilot", Allowance=10000, Unit=UsageUnit.Credits)
    result = map_github(account, {"usageItems": [{"unitType": "ai-credits", "grossQuantity": 8000},
                                                 {"unitType": "credits", "grossQuantity": 200},
                                                 {"unitType": "requests", "grossQuantity": 900}]}, NOW)
    assert result.Windows[0].remaining == 1800 and result.Windows[0].AllowanceSource == AllowanceProvenance.Manual


def test_copilot_december_resets_in_january_and_unknown_units_are_not_zero():
    result = map_github(ACCOUNT, {"usageItems": []}, datetime(2026, 12, 31, 23, 59, tzinfo=timezone.utc))
    assert result.Windows[0].ResetsAt == datetime(2027, 1, 1, tzinfo=timezone.utc)
    unknown = map_github(ACCOUNT, {"usageItems": [{"unitType": "minutes", "grossQuantity": 5}]}, NOW)
    assert unknown.Windows[0].Used is None


def test_managed_quota_pooled_and_personal_budget():
    account = ProviderAccount("g", "GitHub Copilot", "Work", "42", "personal", Unit=UsageUnit.Credits)
    pooled = map_managed_quota(account, {"copilot_plan": "business", "token_based_billing": True, "quota_snapshots": {
        "premium_interactions": {"unlimited": True, "credits_used": 1250, "has_quota": True}}}, NOW)
    assert pooled.Windows[0].Used == 1250 and pooled.Windows[0].Limit is None and "shared budget" in pooled.Message
    budget = map_managed_quota(account, {"copilot_plan": "enterprise", "token_based_billing": True, "quota_snapshots": {
        "premium_interactions": {"unlimited": False, "entitlement": 3000, "quota_remaining": 1000}}}, NOW)
    assert budget.Windows[0].Used == 2000 and budget.Windows[0].Limit == 3000
    assert budget.Windows[0].AllowanceSource == AllowanceProvenance.Provider
    assert map_managed_quota(account, {"copilot_plan": "individual"}, NOW) is None
    assert github_allowance({"copilot_plan": "individual_pro", "token_based_billing": True, "quota_snapshots": {
        "premium_interactions": {"unlimited": False, "entitlement": "1500"}}}, UsageUnit.Credits) == 1500


CLAUDE_NOW = datetime(2026, 10, 26, 18, 0, tzinfo=timezone(timedelta(hours=-5)))


def test_claude_reads_distinct_windows_and_provider_timezone():
    windows = claude.parse_usage("Current session\n 12% used\nResets 8:30pm (America/Chicago)\nCurrent week (all models)\n"
                                 " 31% used\nResets Oct 28, 12am (America/Chicago)\nExtra usage\n", CLAUDE_NOW)
    assert [w.Used for w in windows] == [12, 31]
    assert windows[0].ResetsAt == CLAUDE_NOW + timedelta(hours=2.5)
    assert windows[1].ResetsAt == datetime(2026, 10, 28, 0, 0, tzinfo=timezone(timedelta(hours=-5)))


def test_claude_missing_session_does_not_borrow_weekly():
    windows = claude.parse_usage("Current session\nStarts with your first message\nCurrent week (all models)\n0% used\n", CLAUDE_NOW)
    assert len(windows) == 1 and windows[0].Id == "weekly" and windows[0].ResetsAt is None


@pytest.mark.parametrize("text", ["101% used", "-1% used", "1e2% used", "loading"])
def test_claude_rejects_invalid_usage(text):
    assert claude.parse_usage("Current session\n" + text, CLAUDE_NOW) == []


def test_claude_never_guesses_unknown_zone_or_distant_reset():
    assert claude.parse_usage("Current session\n0% used\nResets 8pm (Unknown/Zone)", CLAUDE_NOW)[0].ResetsAt is None
    assert claude.parse_usage("Current session\n0% used\nResets 11am (America/Chicago)", CLAUDE_NOW)[0].ResetsAt is None


def test_claude_single_line_format():
    windows = claude.parse_usage("Current session: 0% used · resets Oct 26, 8:30pm (America/Chicago)\n"
                                 "Current week (all models): 0% used · resets Oct 28, 12am (America/Chicago)", CLAUDE_NOW)
    assert len(windows) == 2 and windows[0].ResetsAt == CLAUDE_NOW + timedelta(hours=2.5)


DEVIN_OBSERVED = datetime(2026, 12, 27, 12, tzinfo=timezone.utc)


@pytest.mark.parametrize("url", ["example.devinenterprise.com", "https://example.devinenterprise.com/org/research",
                                 "https://example.devinenterprise.com/auth/login?redirect=/private"])
def test_devin_keeps_only_tenant_origin(url):
    assert devin.workspace(url) == "https://example.devinenterprise.com"


@pytest.mark.parametrize("url", ["https://example.devinenterprise.com.evil.invalid", "https://a@work.devinenterprise.com",
                                 "https://work.devinenterprise.com:444", "http://work.devinenterprise.com"])
def test_devin_rejects_untrusted_origins(url):
    with pytest.raises(UserFacingError):
        devin.workspace(url)


def test_devin_overage_and_january_reset():
    reading = devin.parse_usage("sample", ["1,001.5", "900", "1/16 2:00 AM", "America/Chicago"], DEVIN_OBSERVED)
    assert reading.Used == 1001.5 and reading.Limit == 900
    assert reading.Reset == datetime(2027, 1, 16, 8, tzinfo=timezone.utc)


@pytest.mark.parametrize("reset,zone", [("", "America/Chicago"), ("1/16 2:00 AM", "Unavailable"), ("5/16 2:00 AM", "America/Chicago")])
def test_devin_partial_readings_preserve_unknowns(reset, zone):
    reading = devin.parse_usage("sample", ["0", "", reset, zone], DEVIN_OBSERVED)
    assert reading.Used == 0 and reading.Limit is None and reading.Reset is None


@pytest.mark.parametrize("value", ["Loading", "1,2,34", "-1"])
def test_devin_malformed_usage_cannot_become_zero(value):
    with pytest.raises(ProviderAccessError):
        devin.parse_usage("sample", [value, "100", "", ""], DEVIN_OBSERVED)


# ------------------------------------------------------------------ domain rules

def window(used, reset=None, wid="w"):
    return QuotaWindow(wid, "Codex · Weekly", UsageUnit.Percent, used, 100, reset, 10080)


def snapshot(*windows, health=SourceHealth.Healthy, allowed=None):
    return QuotaSnapshot("a", ACCOUNT.Subject, ACCOUNT.Scope, NOW, tuple(windows), None, health, UsageAllowed=allowed)


def test_capacity_states_and_unknown_is_not_zero():
    assert window(None).capacity() == CapacityState.Unknown
    assert window(100).capacity() == CapacityState.Exhausted
    assert window(95).capacity() == CapacityState.Critical
    assert window(75).capacity() == CapacityState.Low
    assert window(55).capacity() == CapacityState.Info
    assert window(10).capacity() == CapacityState.Healthy


def test_threshold_crossing_alerts_once_per_period():
    settings = RadarSettings()
    reset = NOW + timedelta(days=6)
    first = AlertEvaluator.evaluate(ACCOUNT, snapshot(window(60, reset)), snapshot(window(75, reset)), settings, NOW)
    assert [a.Key.rsplit("/", 1)[1] for a in first] == ["low"]
    again = AlertEvaluator.evaluate(ACCOUNT, snapshot(window(75, reset)), snapshot(window(76, reset)), settings, NOW)
    assert again == []
    muted = settings.with_capacity_alerts("a", "w", False)
    assert AlertEvaluator.evaluate(ACCOUNT, snapshot(window(60, reset)), snapshot(window(95, reset)), muted, NOW) == []


def test_restored_requires_provider_confirmation():
    settings = RadarSettings()
    before = snapshot(window(100))
    assert not any(a.Key.endswith("restored") for a in AlertEvaluator.evaluate(ACCOUNT, before, snapshot(window(10)), settings, NOW))
    restored = AlertEvaluator.evaluate(ACCOUNT, before, snapshot(window(10), allowed=True), settings, NOW)
    assert any(a.Key.endswith("restored") for a in restored)


def test_quiet_hours_overnight():
    zone = ZoneInfo("America/Chicago")
    settings = RadarSettings(QuietStartHour=22, QuietEndHour=7)
    assert AlertEvaluator.is_quiet(settings, datetime(2026, 10, 25, 23, tzinfo=zone), zone)
    assert AlertEvaluator.is_quiet(settings, datetime(2026, 10, 25, 3, tzinfo=zone), zone)
    assert not AlertEvaluator.is_quiet(settings, datetime(2026, 10, 25, 12, tzinfo=zone), zone)


def test_reminders_catch_up_to_nearest_due():
    due = TimedReminder.due(TimedReminder.defaults(True), NOW + timedelta(hours=20), NOW)
    assert due.Id == "default-24"
    assert TimedReminder.due(TimedReminder.defaults(True), NOW - timedelta(hours=1), NOW) is None


def test_threshold_moves_keep_order():
    t = AlertThresholds(30, 10, 50)
    assert t.move(True, 5).Warning == 11
    assert t.move(False, 45).Critical == 29
    assert AlertThresholds.remaining_at(25, 100) == 75


def test_refresh_schedule_prefers_earlier_boundary():
    account = replace(ACCOUNT, PollingMinutes=10)
    nxt = next_refresh_after_success(snapshot(window(10, NOW + timedelta(minutes=3))), account, NOW)
    assert nxt == NOW + timedelta(minutes=3)


def test_reconnect_policy_rejects_different_identity():
    with pytest.raises(ReconnectError):
        accept_reconnect(ACCOUNT, replace(ACCOUNT, Subject="other@example.invalid"))
    kept = accept_reconnect(replace(ACCOUNT, Enabled=False, PollingMinutes=7), replace(ACCOUNT, CredentialId="new"))
    assert not kept.Enabled and kept.PollingMinutes == 7 and kept.CredentialId == "new"


def test_monitor_status_picks_limiting_window():
    status = MonitorStatus.evaluate([ACCOUNT], {"a": snapshot(window(20, wid="x"), window(95, wid="y"))}, RadarSettings())
    assert status.Capacity == CapacityState.Critical and status.WindowId == "y"


def test_ordering_normalizes_and_moves():
    assert normalize_order(["b", "gone", "a"], ["a", "b", "c"]) == ["b", "a", "c"]
    assert move_in_order(["a", "b", "c"], "c", 0) == ["c", "a", "b"]


def test_settings_round_trip_and_tolerates_unknown_keys():
    settings = RadarSettings(QuietStartHour=21).with_window_thresholds("a", "w", 40, 15, 60)
    restored = ser.loads(RadarSettings, ser.dumps(settings))
    assert restored == settings
    assert ser.from_plain(RadarSettings, {"Sound": False, "FutureSetting": 1}).Sound is False
