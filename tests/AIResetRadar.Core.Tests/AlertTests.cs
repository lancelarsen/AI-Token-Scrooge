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
}
