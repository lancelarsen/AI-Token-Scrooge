using System.Text.Json;
using Xunit;
namespace AIResetRadar.Core.Tests;
public class TimedReminderTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T12:00:00Z");
    static readonly ProviderAccount Account = new("a", "OpenAI", "Work", "s", "w");
    static QuotaSnapshot Snapshot(DateTimeOffset? at) => new("a", "s", "w", Now,
        [new("weekly", "Weekly", UsageUnit.Percent, 0, 100, at)], new(1, [new("credit", "available", at)]));
    [Fact] public void DefaultsMatchRequestedSchedules()
    {
        var settings = JsonSerializer.Deserialize<RadarSettings>("{}")!;
        Assert.Equal(new double[] {240,120,72,48,24,12,6,3,1}, settings.EffectiveResetReminders.Select(r => r.Hours));
        Assert.Equal(new double[] {120,72,48,24,12,6,3,1}, settings.EffectiveRefreshReminders.Select(r => r.Hours));
        Assert.All(settings.EffectiveResetReminders.Take(3), r => Assert.Equal(ReminderSeverity.Info, r.Severity));
        Assert.All(settings.EffectiveResetReminders.Skip(3), r => Assert.Equal(ReminderSeverity.Critical, r.Severity));
    }
    [Fact] public void IndependentSchedulesRoundTripAndEmptyDisables()
    {
        var settings = new RadarSettings(ResetExpiryReminders: [], QuotaRefreshReminders: [new("custom", 7, "Hours", ReminderSeverity.Warning)]);
        settings = JsonSerializer.Deserialize<RadarSettings>(JsonSerializer.Serialize(settings))!;
        var alert = Assert.Single(AlertEvaluator.Evaluate(Account, null, Snapshot(Now.AddHours(6)), settings, Now));
        Assert.True(alert.IsRefreshReminder); Assert.Equal(ReminderSeverity.Warning, alert.Severity);
        Assert.False(AlertEvaluator.IsEnabled(alert, settings with { QuotaRefreshReminders = [] }));
    }
    [Fact] public void CatchUpSelectsOnlyNearestDueMilestoneAndStableKey()
    {
        var snapshot = Snapshot(Now.AddHours(5));
        var first = AlertEvaluator.Evaluate(Account, null, snapshot, new(), Now);
        Assert.Equal(2, first.Count);
        Assert.All(first, a => Assert.Equal("default-6", a.ReminderId));
        Assert.Equal(first.Select(a => a.Key), AlertEvaluator.Evaluate(Account, snapshot, snapshot, new(), Now.AddMinutes(5)).Select(a => a.Key));
        Assert.All(AlertEvaluator.Evaluate(Account, snapshot, snapshot, new(), Now.AddHours(2)), a => Assert.Equal("default-3", a.ReminderId));
    }
    [Theory] [InlineData(-1)] [InlineData(0)]
    public void ExpiredEventsAreNotReminded(int hours) => Assert.Empty(AlertEvaluator.Evaluate(Account, null, Snapshot(Now.AddHours(hours)), new(), Now));
    [Fact] public void UnknownAndUnhealthyEventsAreNotReminded()
    {
        Assert.Empty(AlertEvaluator.Evaluate(Account, null, Snapshot(null), new(), Now));
        Assert.Empty(AlertEvaluator.Evaluate(Account, null, Snapshot(Now.AddHours(3)) with { Health = SourceHealth.Stale }, new(), Now));
    }
    [Fact] public void ExpirySwitchDoesNotDisableRefreshAndNewPeriodGetsNewKey()
    {
        var settings = new RadarSettings(Rules: [new("a", Expiry: false)]);
        var first = Assert.Single(AlertEvaluator.Evaluate(Account, null, Snapshot(Now.AddHours(3)), settings, Now));
        var second = Assert.Single(AlertEvaluator.Evaluate(Account, null, Snapshot(Now.AddHours(4)), settings, Now));
        Assert.True(first.IsRefreshReminder); Assert.NotEqual(first.Key, second.Key);
    }
    [Fact] public void MovingProviderEstimateDoesNotRepeatSameMilestone()
    {
        var before = Snapshot(Now.AddHours(5));
        var current = Snapshot(Now.AddHours(5).AddMinutes(2)) with { ObservedAt = Now.AddMinutes(2), Reserve = null };
        Assert.Empty(AlertEvaluator.Evaluate(Account, before, current, new(), Now.AddMinutes(2)));
    }
    [Fact] public void InvalidEntriesAreIgnoredAndBoundaryIsInclusive()
    {
        TimedReminder[] list = [new("bad", -2, "Days", ReminderSeverity.Info), new("ok", 3, "Hours", ReminderSeverity.Warning)];
        Assert.Null(TimedReminder.Due(list, Now.AddHours(3).AddSeconds(1), Now));
        Assert.Equal("ok", TimedReminder.Due(list, Now.AddHours(3), Now)!.Id);
    }
}
