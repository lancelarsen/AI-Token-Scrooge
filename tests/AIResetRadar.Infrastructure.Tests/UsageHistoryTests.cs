using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class UsageHistoryTests
{
    static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-22T12:00:00Z");
    static readonly ProviderAccount OpenAi = new("a", "OpenAI", "Personal", "subject", "scope");

    static QuotaWindow Weekly(decimal? remaining = 80, string id = "codex/secondary", DateTimeOffset? reset = null) =>
        new(id, "Display Text Is Not Used", UsageUnit.Percent, remaining is { } n ? 100 - n : null, 100,
            reset, 10080, AllowanceProvenance.Provider);

    static QuotaSnapshot Snapshot(int minutes, QuotaWindow? window = null, ProviderAccount? account = null) =>
        new((account ?? OpenAi).Id, (account ?? OpenAi).Subject, (account ?? OpenAi).Scope, Start.AddMinutes(minutes),
            [window ?? Weekly()], null, PlanType: "plus");

    [Fact]
    public void UsesWeeklyDurationRatherThanPrimarySecondaryOrDisplayName()
    {
        var shortWindow = Weekly(99, "codex/secondary", Start.AddHours(5)) with { DurationMinutes = 300, Name = "Weekly" };
        var weekly = Weekly(60, "codex/primary", Start.AddDays(7));
        var snapshot = Snapshot(0) with { Windows = [shortWindow, weekly] };
        var series = UsageHistory.Build(OpenAi, [snapshot], Start, Start.AddDays(7));
        var point = Assert.Single(series.Points);
        Assert.Equal("codex/primary", point.WindowId);
        Assert.Equal(60, point.RemainingPercent);
        Assert.Equal("Weekly", series.Period);
        Assert.Equal("Plus", series.PlanLabel);
        Assert.Equal(Start.AddDays(7), Assert.Single(series.Events).At);
        Assert.Equal(2, snapshot.Windows.Count);
    }

    [Fact]
    public void SelectsClaudeAllModelsWeeklyAndCopilotMonthly()
    {
        var claude = OpenAi with { Provider = "Claude" };
        var weekly = Weekly(42, "weekly") with { DurationMinutes = null };
        var session = Weekly(88, "session") with { DurationMinutes = 300 };
        Assert.Equal(weekly, UsageHistory.SelectWindow(claude.Provider, [session, weekly]).Window);
        var copilot = OpenAi with { Provider = "GitHub Copilot" };
        var monthly = new QuotaWindow("copilot/monthly", "Anything", UsageUnit.Credits, 250, 1000, null);
        var result = UsageHistory.Build(copilot, [Snapshot(0, monthly, copilot)], Start, Start.AddMinutes(1));
        Assert.Equal("Monthly", result.Period);
        Assert.Equal(75, Assert.Single(result.Points).RemainingPercent);
        Assert.False(UsageHistory.IsEligible("OpenAI", session));
    }

    [Fact]
    public void ChoosesCanonicalCodexBucketButDoesNotAverageAmbiguousWeeklyLimits()
    {
        var product = Weekly(70, "codex/primary");
        var other = Weekly(20, "review/primary");
        Assert.Equal(product, UsageHistory.SelectWindow("OpenAI", [other, product]).Window);
        var ambiguous = Snapshot(0) with { Windows = [product, Weekly(40)] };
        var series = UsageHistory.Build(OpenAi, [ambiguous], Start, Start.AddMinutes(1));
        Assert.Empty(series.Points);
        Assert.Equal("Multiple Weekly Limits Reported", series.Status);
    }

    [Fact]
    public void KeepsAccountsSeparateAcrossRenameAndRejectsDifferentIdentities()
    {
        var second = OpenAi with { Id = "b", Nickname = "Personal" };
        var records = new[] { Snapshot(0), Snapshot(2, Weekly(99), second), Snapshot(4, Weekly(70)) with { Subject = "different" }, Snapshot(6, Weekly(60)) };
        var renamed = OpenAi with { Nickname = "Work" };
        var series = UsageHistory.Build(renamed, records, Start, Start.AddMinutes(7));
        Assert.Equal(new decimal[] { 80, 60 }, series.Points.Select(p => p.RemainingPercent));
        Assert.True(series.Points[1].StartsSegment);
        Assert.Equal("Work", series.Account.Nickname);
    }

    [Fact]
    public void UnresolvedAccountDoesNotStitchTwoReportedWorkspacesTogether()
    {
        var account = OpenAi with { Scope = "unresolved" };
        var result = UsageHistory.Build(account, [Snapshot(0), Snapshot(2, Weekly(99)) with { Scope = "second-workspace" }],
            Start, Start.AddMinutes(3));
        Assert.True(result.Points[1].StartsSegment);
        Assert.Null(result.Points[1].ReplenishedFrom);
    }

    [Fact]
    public void MissingObservationsInterpolateWithoutInventingZeroOrCrossingIdentities()
    {
        var records = new[] {
            Snapshot(0), Snapshot(2, Weekly(null)), Snapshot(4, Weekly(70)),
            Snapshot(6) with { Windows = [] }, Snapshot(8, Weekly(60)),
            Snapshot(10, Weekly(50)) with { Health = SourceHealth.Stale }, Snapshot(12, Weekly(40)),
            Snapshot(14, Weekly(30)) with { Scope = "other" }, Snapshot(16, Weekly(20))
        };
        var series = UsageHistory.Build(OpenAi, records, Start, Start.AddMinutes(17));
        Assert.Equal(new decimal[] { 80, 70, 60, 40, 20 }, series.Points.Select(p => p.RemainingPercent));
        Assert.True(series.Points[0].StartsSegment);
        Assert.All(series.Points.Skip(1).Take(3), p => { Assert.False(p.StartsSegment); Assert.True(p.InterpolatedFromPrevious); });
        Assert.True(series.Points[4].StartsSegment);
        Assert.DoesNotContain(series.Points, p => p.RemainingPercent == 0);
    }

    [Fact]
    public void LongOfflineGapUsesPollCadenceAndDoesNotExtendToToday()
    {
        var records = new[] { Snapshot(0), Snapshot(10, Weekly(70)), Snapshot(31, Weekly(60)) };
        var result = UsageHistory.Build(OpenAi, records, Start, Start.AddHours(2));
        Assert.False(result.Points[1].StartsSegment);
        Assert.False(result.Points[2].StartsSegment);
        Assert.True(result.Points[2].InterpolatedFromPrevious);
        Assert.Equal(Start.AddMinutes(31), result.Latest!.At);
        Assert.Equal("Waiting for Fresh Usage", result.Status);
        var slower = UsageHistory.Build(OpenAi, records, Start, Start.AddMinutes(32), TimeSpan.FromMinutes(10));
        Assert.False(slower.Points[2].StartsSegment);
    }

    [Fact]
    public void AllowanceAndWindowChangesDoNotCreateFalseReplenishment()
    {
        var account = OpenAi with { Provider = "GitHub Copilot" };
        var monthly = new QuotaWindow("copilot/monthly", "Monthly", UsageUnit.Credits, 500, 1000, null);
        var snapshots = new[] { Snapshot(0, monthly, account), Snapshot(2, monthly with { Limit = 2000 }, account) };
        var result = UsageHistory.Build(account, snapshots, Start, Start.AddMinutes(3));
        Assert.True(result.Points[1].StartsSegment);
        Assert.Null(result.Points[1].ReplenishedFrom);
        var windows = UsageHistory.Build(OpenAi, [Snapshot(0), Snapshot(2, Weekly(90, "codex/primary"))], Start, Start.AddMinutes(3));
        Assert.True(windows.Points[1].StartsSegment);
    }

    [Fact]
    public void ReplenishmentKeepsMeasuredNinetyOnePercentAndObservationInterval()
    {
        var result = UsageHistory.Build(OpenAi, [Snapshot(0, Weekly(60)), Snapshot(2, Weekly(91))], Start, Start.AddMinutes(3));
        var replenishment = result.Points[1];
        Assert.Equal(91, replenishment.RemainingPercent);
        Assert.Equal(60, replenishment.ReplenishedFrom);
        Assert.Equal(Start, replenishment.PreviousObservedAt);
        Assert.Equal(Start.AddMinutes(2), replenishment.At);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void RevisedRefreshScheduleReplacesOldScheduleBeforeDueTime()
    {
        var records = new[] {
            Snapshot(0, Weekly(80, reset: Start.AddHours(2))),
            Snapshot(2, Weekly(79, reset: Start.AddHours(3))),
            Snapshot(4, Weekly(78, reset: Start.AddHours(3)))
        };
        var result = UsageHistory.Build(OpenAi, records, Start, Start.AddHours(4));
        var marker = Assert.Single(result.Events);
        Assert.Equal(Start.AddHours(3), marker.At);
        Assert.Equal(HistoryEventKind.ScheduledRefresh, marker.Kind);
        Assert.Equal(Start.AddMinutes(4), marker.ReportedAt);
    }

    [Fact]
    public void SubsequentQuotaCycleRetainsPriorReportedSchedule()
    {
        var records = new[] {
            Snapshot(0, Weekly(10, reset: Start.AddMinutes(1))),
            Snapshot(2, Weekly(91, reset: Start.AddDays(7))),
            Snapshot(4, Weekly(90, reset: Start.AddDays(7)))
        };
        var result = UsageHistory.Build(OpenAi, records, Start, Start.AddDays(8));
        Assert.Equal(new[] { Start.AddMinutes(1), Start.AddDays(7) }, result.Events.Select(e => e.At));
        Assert.All(result.Events, e => Assert.Equal(HistoryEventKind.ScheduledRefresh, e.Kind));
    }

    [Fact]
    public void CreditDisappearanceDoesNotBecomeRedemptionOrArtificialCapacityIncrease()
    {
        var credit = new ResetCredit("credit", "available", Start.AddMinutes(5));
        var records = new[] {
            Snapshot(0, Weekly(60)) with { Reserve = new(1, [credit]) },
            Snapshot(2, Weekly(59)) with { Reserve = new(0, []) },
            Snapshot(6, Weekly(58))
        };
        var result = UsageHistory.Build(OpenAi, records, Start, Start.AddMinutes(7));
        var marker = Assert.Single(result.Events);
        Assert.Equal(HistoryEventKind.ReportedExpiration, marker.Kind);
        Assert.Contains("Reported", marker.Detail);
        Assert.Equal(new decimal[] { 60, 59, 58 }, result.Points.Select(p => p.RemainingPercent));
        Assert.All(result.Points, p => Assert.Null(p.ReplenishedFrom));
    }

    [Fact]
    public void ExplicitRedeemedStatusBeforeExpiryRemovesThatExpirationSchedule()
    {
        var credit = new ResetCredit("credit", "available", Start.AddMinutes(5));
        var result = UsageHistory.Build(OpenAi, [
            Snapshot(0) with { Reserve = new(1, [credit]) },
            Snapshot(2) with { Reserve = new(0, [credit with { Status = "redeemed" }]) }
        ], Start, Start.AddMinutes(7));
        Assert.Empty(result.Events);
    }

    [Fact]
    public void UnknownCopilotAllowanceProducesNoPercentageAndFutureRecordsAreExcluded()
    {
        var account = OpenAi with { Provider = "GitHub Copilot" };
        var window = new QuotaWindow("copilot/monthly", "Monthly", UsageUnit.Credits, 50, null, Start.AddDays(5));
        var result = UsageHistory.Build(account, [Snapshot(0, window, account), Snapshot(10, window with { Limit = 1000 }, account)], Start, Start.AddMinutes(5));
        Assert.Empty(result.Points);
        Assert.Empty(result.Events);
        Assert.Equal("Remaining Capacity Unavailable", result.Status);
    }

    [Fact]
    public void OnlyNearestPrecedingObservationIsRetainedAndNoBackfillIsInvented()
    {
        var result = UsageHistory.Build(OpenAi, [Snapshot(-20), Snapshot(-4, Weekly(79)), Snapshot(2, Weekly(78))], Start, Start.AddMinutes(5));
        Assert.Equal(new[] { Start.AddMinutes(-4), Start.AddMinutes(2) }, result.Points.Select(p => p.At));
        Assert.False(result.Points[1].StartsSegment);
        var old = UsageHistory.Build(OpenAi, [Snapshot(-20)], Start, Start.AddMinutes(5));
        Assert.Empty(old.Points);
    }

    [Fact]
    public void DownsamplingPreservesExtremaGapEdgesAndActualReplenishment()
    {
        var samples = new[] { 90m, 89m, 5m, 88m, 87m, 86m, 85m, 84m, 83m, 82m }
            .Select((value, i) => new HistoryPoint(Start.AddMinutes(i), value, "weekly", i is 0 or 6,
                i > 0 ? Start.AddMinutes(i - 1) : null, i == 3 ? 5 : null)).ToArray();
        var reduced = UsageHistory.Downsample(samples, _ => 12, 0, 20);
        Assert.Contains(samples[0], reduced);
        Assert.Contains(samples[2], reduced);
        Assert.Contains(samples[3], reduced);
        Assert.Contains(samples[5], reduced);
        Assert.Contains(samples[6], reduced);
        Assert.Contains(samples[^1], reduced);
        Assert.True(reduced.Length < samples.Length);
        Assert.Equal(reduced.OrderBy(p => p.At), reduced);
    }

    [Fact]
    public void DrawingNarrowViewportDoesNotProjectAllOffscreenSamples()
    {
        var samples = Enumerable.Range(0, 10000).Select(i => new HistoryPoint(Start.AddMinutes(i), 90 - i / 200m, "weekly", i == 0)).ToArray();
        var projections = 0;
        var result = UsageHistory.Downsample(samples, at => { projections++; return (at - Start).TotalMinutes; }, 4000, 4010);
        Assert.Equal(Start.AddMinutes(3999), result[0].At);
        Assert.Equal(Start.AddMinutes(4011), result[^1].At);
        Assert.InRange(projections, 1, 100);
    }

    [Fact]
    public void DownsamplingPreservesEstimatedSpanAndReturnToMeasuredData()
    {
        var samples = Enumerable.Range(0, 10).Select(i =>
            new HistoryPoint(Start.AddMinutes(i), 90 - i, "weekly", i == 0,
                InterpolatedFromPrevious: i == 4)).ToArray();
        var reduced = UsageHistory.Downsample(samples, _ => 12, 0, 20);
        Assert.Contains(samples[3], reduced);
        Assert.Contains(samples[4], reduced);
        Assert.Contains(samples[5], reduced);
        Assert.True(reduced.Length < samples.Length);
    }
}
