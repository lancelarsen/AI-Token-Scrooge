using Xunit;
namespace AIResetRadar.Core.Tests;

public class QuotaTests
{
    static QuotaWindow Window(decimal? used, decimal? limit = 100, UsageUnit unit = UsageUnit.Percent) =>
        new("weekly", "Codex weekly", unit, used, limit, null);

    [Theory]
    [InlineData(38, 62, CapacityState.Healthy)]
    [InlineData(74, 26, CapacityState.Low)]
    [InlineData(95, 5, CapacityState.Critical)]
    [InlineData(120, 0, CapacityState.Exhausted)]
    public void PercentRemainingAndThresholds(decimal used, decimal left, CapacityState state)
    {
        var window = Window(used);
        Assert.Equal(left, window.RemainingPercent);
        Assert.Equal(state, window.Capacity());
        Assert.InRange(window.Fill, 0, 1);
    }

    [Fact] public void AbsoluteOverageIsPreservedWhileVisualFillIsCapped()
    {
        var window = Window(12_000, 10_000, UsageUnit.Credits);
        Assert.Equal(-2_000, window.Remaining);
        Assert.Equal(120, window.UsedPercent);
        Assert.Equal(1, window.Fill);
    }

    [Fact] public void MissingAllowanceNeverInventsRemaining()
    {
        var window = Window(8_200, null, UsageUnit.Credits);
        Assert.Null(window.Remaining);
        Assert.Null(window.RemainingPercent);
        Assert.Equal(CapacityState.Unknown, window.Capacity());
    }

    [Fact] public void InvalidNegativeUsageIsUnknown()
    {
        Assert.Null(Window(-1).UsedPercent);
        Assert.Null(Window(-1).Remaining);
    }

    [Fact] public void DueResetRequiresConfirmation()
    {
        var now = DateTimeOffset.Parse("2026-10-25T14:46:00Z");
        Assert.Equal("Reset due — checking", ResetTime.Countdown(now.AddSeconds(-1), now));
        Assert.Equal("In 2h 14m", ResetTime.Countdown(now.AddHours(2).AddMinutes(14), now));
        Assert.Equal("Reset time unavailable", ResetTime.Countdown(null, now));
    }

    [Fact] public void ReserveCountSurvivesPartialExpiredOrUnknownDetails()
    {
        var now = DateTimeOffset.Parse("2026-10-25T14:46:00Z");
        var reserve = new ResetReserve(3, [new("expired", "available", now.AddDays(-1)),
            new("active", "available", now.AddDays(4))]);
        Assert.Equal(3, reserve.AvailableCount);
        Assert.True(reserve.HasPartialDetails);
        Assert.Equal(now.AddDays(4), reserve.EarliestExpiry(now));
        Assert.Null(new ResetReserve(2, null).EarliestExpiry(now));
        Assert.Null(new ResetReserve(1, [new("x", "available", null)]).EarliestExpiry(now));
    }
}
