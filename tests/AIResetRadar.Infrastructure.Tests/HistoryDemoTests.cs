using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class HistoryDemoTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T15:00:00Z");

    static (ProviderAccount[], Dictionary<string, QuotaSnapshot>) Fixture()
    {
        ProviderAccount[] accounts = [new("demo-plus", "OpenAI", "Personal", "a", "a"),
            new("demo-copilot", "GitHub Copilot", "Work", "b", "b"),
            new("demo-pro", "OpenAI", "Work", "c", "c"), new("demo-claude", "Claude", "Personal", "d", "d")];
        var snapshots = accounts.Select((account, index) => new QuotaSnapshot(account.Id, account.Subject, account.Scope, Now,
            [new(account.Provider == "GitHub Copilot" ? "copilot/monthly" : account.Provider == "Claude" ? "weekly" : "codex/secondary",
                "Weekly Or Monthly", UsageUnit.Percent, 74 - index * 12, 100,
                account.Provider == "GitHub Copilot" ? DateTimeOffset.Parse("2026-11-01T00:00:00Z") : Now.AddDays(index + 2),
                account.Provider == "GitHub Copilot" ? null : 10080)], null, SourceHealth.Demo, PlanType: index == 0 ? "plus" : "pro"))
            .ToDictionary(s => s.AccountId);
        return (accounts, snapshots);
    }

    [Fact]
    public void FourAccountsHaveDistinctIdentityActualEndpointsAndSevenDaysOfDemoHistory()
    {
        var (accounts, snapshots) = Fixture();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var result = HistoryDemo.Build(accounts, snapshots, Now, zone);
        Assert.Equal(4, result.Length);
        Assert.Equal(2, result.Count(s => s.Account.Provider == "OpenAI"));
        foreach (var series in result)
        {
            Assert.Equal(new HorizonViewport(Now, null, zone, 700).StartAt, series.Points[0].At);
            Assert.Equal(Now, series.Latest!.At);
            Assert.Equal(snapshots[series.Account.Id].Windows[0].RemainingPercent, series.Latest.RemainingPercent);
            Assert.All(series.Points, p => Assert.InRange(p.RemainingPercent, 0, 100));
        }
        Assert.Contains(result.SelectMany(s => s.Points), p => p.ReplenishedFrom.HasValue && p.RemainingPercent is > 90 and < 100);
    }

    [Fact]
    public void DemoGapAndExpirationAreNotArtificialRefills()
    {
        var (accounts, snapshots) = Fixture();
        var result = HistoryDemo.Build(accounts, snapshots, Now, TimeZoneInfo.Utc);
        var claude = result.Single(s => s.Account.Provider == "Claude");
        Assert.Single(claude.Points.Where(p => p.StartsSegment));
        var gapEnd = Assert.Single(claude.Points.Where(p => p.InterpolatedFromPrevious));
        Assert.Null(gapEnd.ReplenishedFrom);
        var plus = result[0];
        var expiry = Assert.Single(plus.Events, e => e.Kind == HistoryEventKind.ReportedExpiration);
        var nextPoint = plus.Points.First(p => p.At > expiry.At);
        Assert.Null(nextPoint.ReplenishedFrom);
        Assert.Empty(result.Single(s => s.Account.Provider == "GitHub Copilot").Events);
    }

    [Fact]
    public void LiveSnapshotsAndShortWindowsCannotProduceFabricatedHistory()
    {
        var (accounts, snapshots) = Fixture();
        foreach (var key in snapshots.Keys.ToArray()) snapshots[key] = snapshots[key] with { Health = SourceHealth.Healthy };
        Assert.Empty(HistoryDemo.Build(accounts, snapshots, Now, TimeZoneInfo.Utc));
        snapshots[accounts[0].Id] = snapshots[accounts[0].Id] with { Health = SourceHealth.Demo,
            Windows = [snapshots[accounts[0].Id].Windows[0] with { DurationMinutes = 300 }] };
        Assert.Empty(HistoryDemo.Build(accounts, snapshots, Now, TimeZoneInfo.Utc));
    }
}
