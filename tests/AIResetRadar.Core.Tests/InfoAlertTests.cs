using Xunit;
namespace AIResetRadar.Core.Tests;
public class InfoAlertTests
{
    [Theory] [InlineData(49, CapacityState.Healthy)] [InlineData(50, CapacityState.Info)] [InlineData(70, CapacityState.Low)] [InlineData(90, CapacityState.Critical)] [InlineData(100, CapacityState.Exhausted)]
    public void UsageProgressesThroughFourLevels(decimal used, CapacityState expected) =>
        Assert.Equal(expected, new QuotaWindow("w", "Weekly", UsageUnit.Percent, used, 100, null).Capacity());
    [Fact] public void InfoAlertHasSeverityAndHonorsBarSwitch()
    {
        var now = DateTimeOffset.UtcNow;
        var account = new ProviderAccount("a", "OpenAI", "Work", "s", "s");
        var current = new QuotaSnapshot("a", "s", "s", now, [new("w", "Weekly", UsageUnit.Percent, 55, 100, null)], null);
        var alert = Assert.Single(AlertEvaluator.Evaluate(account, null, current, new(), now));
        Assert.Equal(ReminderSeverity.Info, alert.Severity);
        Assert.Empty(AlertEvaluator.Evaluate(account, current, current, new(), now));
        Assert.False(AlertEvaluator.IsEnabled(alert, new RadarSettings().WithCapacityAlerts("a", "w", false)));
    }
    [Fact] public void CustomInfoSurvivesSaveAndMarkersStayOrdered()
    {
        var settings = new RadarSettings().WithWindowThresholds("a", "w", 30, 10, 60);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<RadarSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        Assert.Equal(60, loaded.RuleFor("a", "w").InfoThreshold);
        Assert.Equal(30, new AlertThresholds(30, 10, 50).MoveInfo(5).Info);
        Assert.Equal(50, new AlertThresholds(30, 10, 50).Move(true, 80).Warning);
    }
}
