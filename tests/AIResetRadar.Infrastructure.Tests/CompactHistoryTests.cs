using System.Text.Json;
using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public sealed class CompactHistoryTests
{
    [Fact]
    public async Task EveryQuotaWindowAndItsResetTimeArePreservedIncludingSlowerPolling()
    {
        using var files = new HistoryFixture(); var start = files.Now.AddHours(-3);
        var first = files.Snapshot(start) with { Windows = [
            files.Snapshot(start).Windows[0], new("session", "Current Session", UsageUnit.Percent, 12, 100, files.Now.AddHours(2), 300)] };
        await files.Store.RecordSnapshotAsync(first, pollingMinutes: 30);
        await files.Store.RecordSnapshotAsync(first with { ObservedAt = start.AddMinutes(30) }, pollingMinutes: 30);
        var changed = first with { ObservedAt = start.AddMinutes(60), Windows = [first.Windows[0], first.Windows[1] with { Used = 70 }] };
        await files.Store.RecordSnapshotAsync(changed, pollingMinutes: 30);
        var page = await files.Store.HistoryPageAsync("a", start, files.Now);
        Assert.Equal(2, page.Intervals.Count); Assert.Equal(2, page.Intervals[0].ObservationCount);
        Assert.Equal(70, page.Intervals[1].Last.Windows[1].Used);
        Assert.Equal(first.Windows[1].ResetsAt, page.Intervals[0].Last.Windows[1].ResetsAt);
        var graph = UsageHistory.Build(files.Account with { PollingMinutes = 30 }, page.Intervals.SelectMany(i => i.Endpoints()), start, files.Now);
        Assert.False(graph.Points[1].InterpolatedFromPrevious);
        var faster = UsageHistory.Build(files.Account, page.Intervals.SelectMany(i => i.Endpoints()), start, files.Now);
        Assert.True(faster.Points[1].InterpolatedFromPrevious);
    }

    [Fact]
    public async Task RepeatedReadingsBecomeOneCompactRunWithMeasuredEndpoints()
    {
        using var files = new HistoryFixture(); var start = files.Now.AddHours(-4);
        for (var i = 0; i <= 120; i++)
            await files.Store.RecordSnapshotAsync(files.Snapshot(start.AddMinutes(i * 2)) with { ProviderDataAt = start.AddMinutes(i * 2).AddSeconds(-10) });
        var page = await files.Store.HistoryPageAsync("a", start, files.Now);
        var run = Assert.Single(page.Intervals);
        Assert.Equal(121, run.ObservationCount); Assert.Equal(start, run.First.ObservedAt); Assert.Equal(files.Now, run.Last.ObservedAt);
        Assert.Equal(files.Now.AddSeconds(-10), run.Last.ProviderDataAt);
        await using var db = await files.OpenAsync(); await using var command = db.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM history_definitions"; Assert.Equal(1L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT value FROM history";
        var json = (string)(await command.ExecuteScalarAsync())!;
        Assert.DoesNotContain("sample-user", json); Assert.DoesNotContain("scope", json); Assert.DoesNotContain("Windows", json);
        Assert.True(json.Length < 220);
        var graph = UsageHistory.Build(files.Account, run.Endpoints(), start, files.Now);
        Assert.Equal(2, graph.Points.Length); Assert.False(graph.Points[^1].InterpolatedFromPrevious);
        Assert.All(graph.Points, p => Assert.Equal(80, p.RemainingPercent));
    }

    [Fact]
    public async Task ChangesPreservePeaksDropsAllowanceChangesAndResetEvents()
    {
        using var files = new HistoryFixture(); var start = files.Now.AddMinutes(-20);
        var due = start.AddMinutes(6); var expiry = start.AddMinutes(11);
        var originals = new[] {
            files.Snapshot(start) with { Windows = [new("codex/weekly", "Weekly", UsageUnit.Percent, 20.1234567890123456789m, 100, due, 10080)], Reserve = new(1, [new("c", "available", expiry)]) },
            files.Snapshot(start.AddMinutes(2)) with { Windows = [new("codex/weekly", "Weekly", UsageUnit.Percent, 80, 100, due, 10080)], Reserve = new(1, [new("c", "available", expiry)]) },
            files.Snapshot(start.AddMinutes(4)) with { Windows = [new("codex/weekly", "Weekly", UsageUnit.Percent, 90, 100, due, 10080)], Reserve = new(1, [new("c", "available", expiry)]) },
            files.Snapshot(start.AddMinutes(7)) with { Windows = [new("codex/weekly", "Weekly", UsageUnit.Percent, 5, 100, due.AddDays(7), 10080)], Reserve = new(1, [new("c", "available", expiry)]) },
            files.Snapshot(start.AddMinutes(12)) with { Windows = [new("codex/weekly", "Weekly", UsageUnit.Percent, 5, 200, due.AddDays(7), 10080)], Reserve = new(0, [new("c", "expired", expiry)]) }
        };
        foreach (var snapshot in originals) await files.Store.RecordSnapshotAsync(snapshot);
        var restored = await files.Store.HistoryAsync("a", start, files.Now);
        Assert.Equal(originals.Select(s => JsonSerializer.Serialize(s)), restored.Select(s => JsonSerializer.Serialize(s)));
        var expected = UsageHistory.Build(files.Account, originals, start, files.Now);
        var actual = UsageHistory.Build(files.Account, restored, start, files.Now);
        Assert.Equal(expected.Points, actual.Points); Assert.Equal(expected.Events, actual.Events);
        Assert.Contains(actual.Events, e => e.At == due && e.Kind == HistoryEventKind.ScheduledRefresh);
        Assert.Contains(actual.Events, e => e.At == expiry && e.Kind == HistoryEventKind.ReportedExpiration);
        Assert.True(actual.Points[^1].StartsSegment);
    }

    [Fact]
    public async Task ResetBoundaryAndMissingPollsPreventFalseContinuousRuns()
    {
        using var files = new HistoryFixture(); var start = files.Now.AddHours(-2);
        var first = files.Snapshot(start) with { Windows = [files.Snapshot(start).Windows[0] with { ResetsAt = start.AddMinutes(3) }] };
        await files.Store.RecordSnapshotAsync(first);
        await files.Store.RecordSnapshotAsync(first with { ObservedAt = start.AddMinutes(2) });
        await files.Store.RecordSnapshotAsync(first with { ObservedAt = start.AddMinutes(4) });
        await files.Store.RecordSnapshotAsync(first with { ObservedAt = start.AddHours(1) });
        var runs = (await files.Store.HistoryPageAsync("a", start, files.Now)).Intervals;
        Assert.Equal(3, runs.Count); Assert.Equal(2, runs[0].ObservationCount);
        var graph = UsageHistory.Build(files.Account, runs.SelectMany(r => r.Endpoints()), start, files.Now);
        Assert.True(graph.Points[^1].InterpolatedFromPrevious);
    }

    [Fact]
    public async Task DeltaReadAndCacheReplaceAnExtendedEndpointWithoutRereadingOldRuns()
    {
        using var files = new HistoryFixture(); var start = files.Now.AddMinutes(-20);
        await files.Store.RecordSnapshotAsync(files.Snapshot(start));
        var initial = await files.Store.HistoryPageAsync("a", start, files.Now);
        var cache = new UsageHistoryCache(files.Account, start, 0, []).Append(files.Account, start, initial);
        await files.Store.RecordSnapshotAsync(files.Snapshot(start.AddMinutes(2)));
        var delta = await files.Store.HistoryPageAsync("a", start, files.Now, cache.Revision);
        Assert.Equal(initial.Intervals[0].Key, Assert.Single(delta.Intervals).Key);
        cache = cache.Append(files.Account, start, delta);
        await files.Store.RecordSnapshotAsync(files.Snapshot(start.AddMinutes(4)));
        delta = await files.Store.HistoryPageAsync("a", start, files.Now, cache.Revision);
        cache = cache.Append(files.Account, start, delta);
        Assert.DoesNotContain(cache.Build(files.Now).Points, p => p.At == start.AddMinutes(2));
        await files.Store.RecordSnapshotAsync(files.Snapshot(start.AddMinutes(6)) with { Windows = [files.Snapshot(start).Windows[0] with { Used = 55 }] });
        delta = await files.Store.HistoryPageAsync("a", start, files.Now, cache.Revision);
        Assert.Equal(start.AddMinutes(6), Assert.Single(delta.Intervals).First.ObservedAt);
        cache = cache.Append(files.Account, start, delta);
        Assert.Equal(2, cache.Intervals.Length);
        Assert.Equal(new[] { start, start.AddMinutes(4), start.AddMinutes(6) }, cache.Build(files.Now).Points.Select(p => p.At));
        Assert.Empty((await files.Store.HistoryPageAsync("a", start, files.Now, cache.Revision)).Intervals);
        Assert.False(cache.Covers(files.Account with { Subject = "another" }, start));
        Assert.False(cache.Covers(files.Account, start.AddDays(-1)));
    }

    [Fact]
    public async Task FlatRunCrossingDisplayBoundaryStillHasContinuousCoverage()
    {
        using var files = new HistoryFixture(); var start = files.Now.AddHours(-4);
        for (var i = 0; i <= 120; i++) await files.Store.RecordSnapshotAsync(files.Snapshot(start.AddMinutes(i * 2)));
        var from = files.Now.AddHours(-1);
        var page = await files.Store.HistoryPageAsync("a", from, files.Now);
        var cache = new UsageHistoryCache(files.Account, from, 0, []).Append(files.Account, from, page);
        var graph = cache.Build(files.Now);
        Assert.Equal(2, graph.Points.Length); Assert.True(graph.Points[0].At < from);
        Assert.False(graph.Points[^1].InterpolatedFromPrevious);
    }

    [Fact]
    public async Task LegacyUpgradeCreatesBackupAndKeepsStateAndGraphMeaning()
    {
        using var files = new HistoryFixture(); Directory.CreateDirectory(Path.GetDirectoryName(files.Database)!);
        var first = files.Snapshot(files.Now.AddMinutes(-10));
        var last = first with { ObservedAt = files.Now.AddMinutes(-2), Windows = [first.Windows[0] with { Used = 40 }] };
        await SeedLegacy(files, [first, first with { ObservedAt = first.ObservedAt.AddMinutes(2) }, last]);
        var page = await files.Store.HistoryPageAsync("a", first.ObservedAt, files.Now);
        Assert.Equal(2, page.Intervals.Count); Assert.Equal(3, page.Intervals.Sum(i => i.ObservationCount));
        Assert.True(File.Exists(files.Database + ".before-history-v3.bak"));
        Assert.Equal("saved", await files.Store.ReadAsync<string>("fixture"));
        var reopened = new LocalStore(files.Database, files.Clock);
        Assert.Equal(2, (await reopened.HistoryPageAsync("a", first.ObservedAt, files.Now)).Intervals.Count);
        await using var backup = new SqliteConnection($"Data Source={files.Database}.before-history-v3.bak;Mode=ReadOnly");
        await backup.OpenAsync(); await using var command = backup.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM history"; Assert.Equal(3L, await command.ExecuteScalarAsync());
        command.CommandText = "PRAGMA user_version"; Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task FailedMigrationRollsBackAllSchemaAndHistoryChanges()
    {
        using var files = new HistoryFixture(); Directory.CreateDirectory(Path.GetDirectoryName(files.Database)!);
        await SeedLegacy(files, [files.Snapshot(files.Now)]);
        await files.ExecuteAsync("CREATE TRIGGER reject_history BEFORE INSERT ON history BEGIN SELECT RAISE(ABORT,'migration failure'); END");
        await Assert.ThrowsAsync<SqliteException>(() => files.Store.HistoryPageAsync("a", files.Now, files.Now));
        await using var db = await files.OpenAsync(); await using var command = db.CreateCommand();
        command.CommandText = "PRAGMA user_version"; Assert.Equal(2L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM history"; Assert.Equal(1L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('history') WHERE name='last_at'";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    static async Task SeedLegacy(HistoryFixture files, IEnumerable<QuotaSnapshot> snapshots)
    {
        await using var db = await files.OpenAsync(); await using var command = db.CreateCommand();
        command.CommandText = "CREATE TABLE state(key TEXT PRIMARY KEY,value TEXT NOT NULL); CREATE TABLE history(account_id TEXT NOT NULL,observed_at INTEGER NOT NULL,value TEXT NOT NULL,PRIMARY KEY(account_id,observed_at)); PRAGMA user_version=2; INSERT INTO state VALUES('fixture','\"saved\"');";
        await command.ExecuteNonQueryAsync();
        foreach (var snapshot in snapshots) await files.RawSnapshotAsync(snapshot.AccountId, snapshot.ObservedAt, JsonSerializer.Serialize(snapshot));
    }
}
