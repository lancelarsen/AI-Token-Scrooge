using Xunit;
namespace AIResetRadar.Core.Tests;

public class AlertThresholdsTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(350, 30)]
    [InlineData(450, 10)]
    [InlineData(500, 0)]
    [InlineData(-20, 100)]
    [InlineData(600, 0)]
    public void DragPositionsUseRemainingCapacity(double x, decimal expected) =>
        Assert.Equal(expected, AlertThresholds.RemainingAt(x, 500));

    [Fact] public void HandlesCannotCrossOrLeaveThePercentageRange()
    {
        var initial = new AlertThresholds(30, 10);
        Assert.Equal(new(11, 10), initial.Move(true, 0));
        Assert.Equal(new(30, 29), initial.Move(false, 100));
        Assert.Equal(new(100, 10), initial.Move(true, 120));
        Assert.Equal(new(30, 0), initial.Move(false, -20));
    }
    [Fact] public void DraggingOneSectionPreservesAlertFlagsAndOtherSections()
    {
        var settings = new RadarSettings(Rules: [new("a", Low: false, Restored: false), new("a", "weekly", LowThreshold: 45, Critical: false)])
            .WithCapacityAlerts("a", "weekly", false);
        var changed = settings.WithWindowThresholds("a", "weekly", 40, 15);
        var rule = changed.RuleFor("a", "weekly");
        Assert.Equal(40, rule.LowThreshold); Assert.Equal(15, rule.CriticalThreshold); Assert.False(rule.Critical);
        Assert.Equal(30, changed.RuleFor("a", "short").LowThreshold);
        Assert.Equal(30, changed.RuleFor("b", "weekly").LowThreshold);
        Assert.False(changed.CapacityAlertsEnabled("a", "weekly"));
        var shortRule = settings.WithWindowThresholds("a", "short", 35, 12).RuleFor("a", "short");
        Assert.False(shortRule.Low); Assert.False(shortRule.Restored);
    }
    [Fact] public void ExistingFractionalLevelsCanStillBeDraggedInWholePercentSteps()
    {
        Assert.Equal(new AlertThresholds(100, 99.5m), new AlertThresholds(100, 99.5m).Move(true, 20));
        Assert.Equal(new AlertThresholds(.5m, 0), new AlertThresholds(.5m, 0).Move(false, 20));
    }
    [Theory]
    [InlineData(10, 10)]
    [InlineData(5, 10)]
    [InlineData(101, 10)]
    [InlineData(30, -1)]
    public void InvalidThresholdsAreRejected(decimal low, decimal critical) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RadarSettings().WithWindowThresholds("a", "weekly", low, critical));

    [Fact] public void SavedDragValuesDriveActualAlertEvaluation()
    {
        var now = DateTimeOffset.UtcNow;
        var account = new ProviderAccount("a", "OpenAI", "Personal", "qa", "personal");
        var snapshot = new QuotaSnapshot("a", "qa", "personal", now,
            [new("weekly", "Weekly", UsageUnit.Percent, 65, 100, now.AddDays(20))], null);
        Assert.EndsWith("/info", Assert.Single(AlertEvaluator.Evaluate(account, null, snapshot, new(), now)).Key);
        var settings = new RadarSettings().WithWindowThresholds("a", "weekly", 40, 15);
        var reopened = System.Text.Json.JsonSerializer.Deserialize<RadarSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        Assert.Contains("/low", Assert.Single(AlertEvaluator.Evaluate(account, null, snapshot, reopened, now)).Key);
    }
}
