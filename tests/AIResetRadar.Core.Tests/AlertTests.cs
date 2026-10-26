using Xunit;
namespace AIResetRadar.Core.Tests;
public class AlertTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T14:46:00Z");
    static readonly ProviderAccount Account = new("a", "OpenAI", "Personal", "sample", "workspace");
    static QuotaSnapshot Snapshot(decimal used, DateTimeOffset? reset = null, SourceHealth health = SourceHealth.Healthy, bool? allowed = null) =>
        new("a", "sample", "workspace", Now, [new("weekly", "Codex weekly", UsageUnit.Percent, used, 100, reset ?? Now.AddDays(2))], null, health, UsageAllowed: allowed);

    [Fact] public void MultiThresholdJumpEmitsOnlyMostSevereEvent()
    {
        var alert = Assert.Single(AlertEvaluator.Evaluate(Account, Snapshot(10), Snapshot(98), new(), Now));
        Assert.Contains("critical", alert.Key);
    }
    [Fact] public void StaleDataDoesNotClaimALiveThresholdCrossing() =>
        Assert.Empty(AlertEvaluator.Evaluate(Account, Snapshot(10), Snapshot(98, health: SourceHealth.Offline), new(), Now));
    [Fact] public void ElapsedResetDoesNotGenerateRestoration()
    {
        Assert.DoesNotContain(AlertEvaluator.Evaluate(Account, Snapshot(100, Now.AddHours(-1)), Snapshot(100, Now.AddHours(-1)), new(), Now), e => e.Key.Contains("restored"));
    }
    [Fact] public void ProviderPausePreventsRestorationClaim()
    {
        Assert.DoesNotContain(AlertEvaluator.Evaluate(Account, Snapshot(100), Snapshot(0, allowed: false), new(), Now), e => e.Key.Contains("restored"));
    }
    [Fact] public void ConfirmedRestorationTargetsTheExactAccountAndWindowOnlyOnce()
    {
        var before = Snapshot(100);
        var current = Snapshot(20, allowed: true);
        var restored = Assert.Single(AlertEvaluator.Evaluate(Account, before, current, new(), Now));
        Assert.EndsWith("/restored", restored.Key);
        Assert.Equal(Account.Id, restored.AccountId);
        Assert.Equal("weekly", restored.WindowId);
        Assert.Empty(AlertEvaluator.Evaluate(Account, current, current, new(), Now.AddMinutes(1)));
        var other = Account with { Id = "other-account" };
        var otherAlert = Assert.Single(AlertEvaluator.Evaluate(other, before with { AccountId = other.Id }, current with { AccountId = other.Id }, new(), Now));
        Assert.NotEqual(restored.Key, otherAlert.Key);
    }

    [Fact] public void UnknownUsagePermissionCannotClaimRestoration()
    {
        Assert.DoesNotContain(AlertEvaluator.Evaluate(Account, Snapshot(100), Snapshot(0), new(), Now), e => e.Key.EndsWith("/restored"));
    }

    [Theory]
    [InlineData("2026-11-01T06:30:00Z", 1, 2, true)]
    [InlineData("2026-11-01T07:30:00Z", 1, 2, true)]
    [InlineData("2026-11-01T08:00:00Z", 1, 2, false)]
    [InlineData("2026-03-08T07:59:00Z", 1, 3, true)]
    [InlineData("2026-03-08T08:00:00Z", 1, 3, false)]
    public void QuietHoursFollowLocalClockAcrossDaylightSavingTransitions(string instant, int start, int end, bool quiet)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        Assert.Equal(quiet, AlertEvaluator.IsQuiet(new(QuietHours: true, QuietStartHour: start, QuietEndHour: end), DateTimeOffset.Parse(instant), zone));
    }

    [Fact] public void ExhaustedReserveSuppressesOldAvailableCreditDetails()
    {
        var snapshot = Snapshot(10) with { Reserve = new(0, [new("old", "available", Now.AddMinutes(15))]) };
        Assert.Empty(AlertEvaluator.Evaluate(Account, null, snapshot, new(), Now));
    }
    [Fact] public void UnknownCreditExpiryNeverCreatesReminder()
    {
        var snapshot = Snapshot(10) with { Reserve = new(2, [new("x", "available", null)]) };
        Assert.Empty(AlertEvaluator.Evaluate(Account, null, snapshot, new(), Now));
    }
    [Fact] public void CreditReminderKeyIsStableAcrossRepeatedRefreshes()
    {
        var snapshot = Snapshot(10) with { Reserve = new(2, [new("x", "available", Now.AddMinutes(45))]) };
        var first = Assert.Single(AlertEvaluator.Evaluate(Account, null, snapshot, new(), Now));
        var second = Assert.Single(AlertEvaluator.Evaluate(Account, snapshot, snapshot, new(), Now.AddMinutes(1)));
        Assert.Equal(first.Key, second.Key);
    }
    [Fact] public void QuietHoursCrossMidnightInTheUsersZone()
    {
        var settings = new RadarSettings(QuietHours: true, QuietStartHour: 22, QuietEndHour: 8);
        var midnight = new DateTimeOffset(Now.UtcDateTime.Date, TimeSpan.Zero);
        Assert.True(AlertEvaluator.IsQuiet(settings, midnight.AddHours(23), TimeZoneInfo.Utc));
        Assert.True(AlertEvaluator.IsQuiet(settings, midnight.AddHours(7), TimeZoneInfo.Utc));
        Assert.False(AlertEvaluator.IsQuiet(settings, midnight.AddHours(12), TimeZoneInfo.Utc));
    }

    [Fact] public void ImprovingFromCriticalToLowDoesNotProduceAnotherWarning() =>
        Assert.Empty(AlertEvaluator.Evaluate(Account, Snapshot(98), Snapshot(75), new(), Now));

    [Fact] public void ANewPeriodCanProduceOneInitialLowWarning() =>
        Assert.Single(AlertEvaluator.Evaluate(Account, Snapshot(75, Now.AddHours(-1)), Snapshot(75), new(), Now));

    [Fact] public void WindowRulesOverrideAccountDefaultsAndLeaveOtherAccountsAlone()
    {
        var settings = new RadarSettings(Rules: [new("a", LowThreshold: 50), new("a", "weekly", LowThreshold: 20, Low: false)]);
        Assert.Equal(20, settings.RuleFor("a", "weekly").LowThreshold);
        Assert.Equal(50, settings.RuleFor("a", "short").LowThreshold);
        Assert.Equal(30, settings.RuleFor("b", "weekly").LowThreshold);
        Assert.Empty(AlertEvaluator.Evaluate(Account, null, Snapshot(85), settings, Now));
    }

    [Fact] public void DisablingLowWarningsDoesNotDisableExhaustedOrAnotherAccount()
    {
        var settings = new RadarSettings(Rules: [new("a", Low: false)]);
        Assert.Empty(AlertEvaluator.Evaluate(Account, null, Snapshot(80), settings, Now));
        Assert.Single(AlertEvaluator.Evaluate(Account, null, Snapshot(100), settings, Now));
        Assert.Single(AlertEvaluator.Evaluate(Account with { Id = "b" }, null, Snapshot(80) with { AccountId = "b" }, settings, Now));
    }

    [Fact] public void ExpiryRulesAreScopedToTheAccount()
    {
        var snapshot = Snapshot(10) with { Reserve = new(1, [new("c", "available", Now.AddMinutes(30))]) };
        Assert.Empty(AlertEvaluator.Evaluate(Account, null, snapshot, new(Rules: [new("a", Expiry: false)]), Now));
    }

    [Fact] public void DefaultChangesCannotInvertInheritedWindowThresholds()
    {
        var settings = new RadarSettings(Rules: [new("a", "weekly", LowThreshold: 20)]);
        Assert.True(settings.HasValidThresholds);
        Assert.False((settings with { CriticalThreshold = 25 }).HasValidThresholds);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<RadarSettings>("{\"LowThreshold\":40,\"CriticalThreshold\":10}")!;
        Assert.Null(loaded.Rules); Assert.Equal(40, loaded.RuleFor("a", "weekly").LowThreshold);
    }
}
