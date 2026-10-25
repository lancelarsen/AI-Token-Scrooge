using Xunit;
namespace AIResetRadar.Core.Tests;
public class RefreshScheduleTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T14:46:00Z");
    static QuotaSnapshot Snapshot(DateTimeOffset reset, ResetReserve? reserve = null) => new("a", "subject", "scope", Now,
        [new("weekly", "Weekly", UsageUnit.Percent, 80, 100, reset)], reserve);

    [Fact] public void KnownResetInterruptsOrdinaryPollingInterval() =>
        Assert.Equal(Now.AddSeconds(20), RefreshSchedule.AfterSuccess(Snapshot(Now.AddSeconds(20)), "OpenAI", Now));
    [Fact] public void EarnedResetExpiryInterruptsOrdinaryPollingInterval() =>
        Assert.Equal(Now.AddSeconds(10), RefreshSchedule.AfterSuccess(Snapshot(Now.AddDays(2),
            new(1, [new("c", "available", Now.AddSeconds(10))])), "OpenAI", Now));
    [Fact] public void AlreadyPassedBoundaryCannotCreateAnImmediateRetryLoop() =>
        Assert.Equal(Now.AddSeconds(120), RefreshSchedule.AfterSuccess(Snapshot(Now.AddSeconds(-1)), "OpenAI", Now));
    [Fact] public void CopilotKeepsItsSlowerBillingPollingInterval() =>
        Assert.Equal(Now.AddSeconds(300), RefreshSchedule.AfterSuccess(Snapshot(Now.AddDays(2)), "GitHub Copilot", Now));
}
