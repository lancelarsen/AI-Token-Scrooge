using Xunit;
namespace AIResetRadar.Core.Tests;
public class ResetSummaryTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T14:46:00Z");
    static readonly ProviderAccount Account = new("a", "OpenAI", "Work", "sample", "scope");
    [Fact] public void KnownExpiryIsVisibleEvenWhenCountIsUnavailable()
    {
        var snapshot = new QuotaSnapshot("a", "sample", "scope", Now, [], new(null, [new("c", "available", Now.AddHours(2))]));
        var summary = ResetSummary.Evaluate([Account], new Dictionary<string, QuotaSnapshot> { ["a"] = snapshot }, Now);
        Assert.Null(summary.ReportedCount); Assert.Equal(Now.AddHours(2), summary.EarliestExpiry); Assert.True(summary.IsLastReported);
    }
    [Fact] public void LocalExpiryPreservesCountAndPausedAccountsAreExcluded()
    {
        var snapshot = new QuotaSnapshot("a", "sample", "scope", Now, [], new(2, [new("c", "available", Now.AddMinutes(-1)), new("d", "available", Now.AddHours(2))]));
        var snapshots = new Dictionary<string, QuotaSnapshot> { ["a"] = snapshot };
        var summary = ResetSummary.Evaluate([Account], snapshots, Now);
        Assert.Equal(2, summary.ReportedCount); Assert.True(summary.HasExpiredDetails); Assert.True(summary.IsLastReported);
        Assert.Equal(Now.AddHours(2), summary.EarliestExpiry);
        Assert.Null(ResetSummary.Evaluate([Account with { Enabled = false }], snapshots, Now).ReportedCount);
    }
    [Fact] public void NonAvailableDetailsDoNotCoverTheReportedAvailableReserve()
    {
        var reserve = new ResetReserve(2, [new("a", "available", Now.AddHours(1)), new("b", "redeeming", Now.AddHours(2))]);
        Assert.True(reserve.HasPartialDetails);
    }
    [Fact] public void AuthoritativeZeroSuppressesExpiryRemindersForContradictoryDetails()
    {
        var reserve = new ResetReserve(0, [new("c", "available", Now.AddMinutes(30))]);
        var snapshot = new QuotaSnapshot("a", "sample", "scope", Now, [], reserve);
        Assert.Null(reserve.EarliestExpiry(Now));
        Assert.Empty(AlertEvaluator.Evaluate(Account, null, snapshot, new(), Now));
    }
}
