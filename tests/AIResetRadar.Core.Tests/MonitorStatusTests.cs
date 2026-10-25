using Xunit;
namespace AIResetRadar.Core.Tests;
public class MonitorStatusTests
{
    static readonly ProviderAccount Account = new("a", "OpenAI", "Work", "sample", "scope");
    static QuotaSnapshot Snapshot(decimal? used = 20, SourceHealth health = SourceHealth.Healthy, bool? allowed = null) =>
        new("a", "sample", "scope", DateTimeOffset.UtcNow, [new("weekly", "Weekly", UsageUnit.Percent, used, 100, null)], null, health, UsageAllowed: allowed);

    [Fact] public void AllPausedAccountsCannotProduceAHealthyIcon()
    {
        var status = MonitorStatus.Evaluate([Account with { Enabled = false }], new Dictionary<string, QuotaSnapshot> { ["a"] = Snapshot() }, new());
        Assert.Equal(CapacityState.Unknown, status.Capacity); Assert.Equal("Monitoring paused", status.Notice);
    }
    [Fact] public void MissingOrUnknownQuotaCannotProduceAHealthyIcon()
    {
        var missing = MonitorStatus.Evaluate([Account], new Dictionary<string, QuotaSnapshot>(), new());
        var unknown = MonitorStatus.Evaluate([Account], new Dictionary<string, QuotaSnapshot> { ["a"] = Snapshot(null) }, new());
        Assert.True(missing.HasUnavailableData); Assert.True(unknown.HasUnavailableData);
        Assert.Equal(CapacityState.Unknown, missing.Capacity); Assert.Equal(CapacityState.Unknown, unknown.Capacity);
    }
    [Fact] public void ProviderPauseIsAttentionEvenWhenTheReportedBarHasSpace()
    {
        var status = MonitorStatus.Evaluate([Account], new Dictionary<string, QuotaSnapshot> { ["a"] = Snapshot(20, allowed: false) }, new());
        Assert.True(status.UsagePaused); Assert.Contains("paused", status.Notice!);
    }
    [Fact] public void StaleExhaustionDoesNotClaimLiveExhaustion()
    {
        var status = MonitorStatus.Evaluate([Account], new Dictionary<string, QuotaSnapshot> { ["a"] = Snapshot(100, SourceHealth.Offline) }, new());
        Assert.Equal(CapacityState.Unknown, status.Capacity); Assert.True(status.HasUnavailableData);
    }
    [Fact] public void FreshExhaustionAndUnavailableOtherSourceAreBothPreserved()
    {
        var status = MonitorStatus.Evaluate([Account, Account with { Id = "b" }], new Dictionary<string, QuotaSnapshot> { ["a"] = Snapshot(100) }, new());
        Assert.Equal(CapacityState.Exhausted, status.Capacity); Assert.True(status.HasUnavailableData); Assert.Equal("a", status.AccountId);
    }
    [Fact] public void TooltipSelectsTheWindowThatTriggeredCustomWarningRules()
    {
        var snapshot = Snapshot() with { Windows = [new("short", "Short", UsageUnit.Percent, 80, 100, null), new("weekly", "Weekly", UsageUnit.Percent, 70, 100, null)] };
        var status = MonitorStatus.Evaluate([Account], new Dictionary<string, QuotaSnapshot> { ["a"] = snapshot },
            new(Rules: [new("a", "short", LowThreshold: 10, CriticalThreshold: 5), new("a", "weekly", LowThreshold: 40)]));
        Assert.Equal(CapacityState.Low, status.Capacity); Assert.Equal("weekly", status.WindowId);
    }
}
