using System.Text.Json;
using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public sealed class HistoryRangeTests
{
    [Fact]
    public async Task RangeIsInclusiveChronologicalAndIsolatedAcrossReopen()
    {
        using var files = new HistoryFixture(); var store = files.Store;
        var start = files.Now.AddHours(-2); var end = files.Now.AddHours(-1);
        foreach (var at in new[] { end.AddMilliseconds(1), end, start.AddMinutes(30), start, start.AddMilliseconds(-1) })
            await store.RecordSnapshotAsync(files.Snapshot(at));
        await store.RecordSnapshotAsync(files.Snapshot(start) with { AccountId = "b", Scope = "other" });
        var values = await new LocalStore(files.Database, files.Clock).HistoryAsync("a", start, end);
        Assert.Equal(new[] { start, start.AddMinutes(30), end }, values.Select(s => s.ObservedAt));
        Assert.All(values, snapshot => Assert.Equal("scope", snapshot.Scope));
        Assert.Single(await store.HistoryAsync("b", start, end));
    }

    [Fact]
    public async Task PrecedingSampleIsOptionalAndNeverIncludesFutureData()
    {
        using var files = new HistoryFixture(); var start = files.Now.AddHours(-2);
        foreach (var at in new[] { start.AddHours(-2), start.AddMinutes(-1), start.AddHours(1) })
            await files.Store.RecordSnapshotAsync(files.Snapshot(at));
        Assert.Empty(await files.Store.HistoryAsync("a", start, start.AddMinutes(30)));
        Assert.Equal(start.AddMinutes(-1), Assert.Single(await files.Store.HistoryAsync("a", start, start.AddMinutes(30), includePreceding: true)).ObservedAt);
        Assert.Empty(await files.Store.HistoryAsync("missing", start, files.Now, includePreceding: true));
    }

    [Fact]
    public async Task BoundsRespectOriginalSubMillisecondTimestamps()
    {
        using var files = new HistoryFixture(); var first = files.Now.AddHours(-1).AddTicks(100);
        var second = first.AddMilliseconds(1); var last = second.AddMilliseconds(1);
        foreach (var at in new[] { first, second, last }) await files.Store.RecordSnapshotAsync(files.Snapshot(at));
        var from = first.AddTicks(1); var to = last.AddTicks(-1);
        Assert.Equal(second, Assert.Single(await files.Store.HistoryAsync("a", from, to)).ObservedAt);
        Assert.Equal(new[] { first, second }, (await files.Store.HistoryAsync("a", from, to, true)).Select(s => s.ObservedAt));
    }

    [Fact]
    public async Task CompleteRangeIsNotTruncatedAtTenThousandRecords()
    {
        using var files = new HistoryFixture(); await files.Store.WriteAsync("initialized", true);
        var start = files.Now.AddHours(-1); const int count = 10005;
        await using (var connection = await files.OpenAsync())
        {
            using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT INTO history(account_id,observed_at,value) VALUES('a',$observed,$value)";
            var observed = command.Parameters.Add("$observed", SqliteType.Integer);
            var value = command.Parameters.Add("$value", SqliteType.Text);
            for (var i = 0; i < count; i++)
            {
                var sample = files.Snapshot(start.AddMilliseconds(i));
                observed.Value = sample.ObservedAt.ToUnixTimeMilliseconds(); value.Value = JsonSerializer.Serialize(sample);
                await command.ExecuteNonQueryAsync();
            }
            transaction.Commit();
        }
        var values = await files.Store.HistoryAsync("a", start, files.Now);
        Assert.Equal(count, values.Count);
        Assert.Equal(start, values[0].ObservedAt);
        Assert.Equal(start.AddMilliseconds(count - 1), values[^1].ObservedAt);
    }

    [Fact]
    public async Task InvalidRangeRowsBecomeGapsWhilePrecedingReadStillFindsUsableObservation()
    {
        using var files = new HistoryFixture(); var start = files.Now.AddHours(-1);
        await files.Store.RecordSnapshotAsync(files.Snapshot(start.AddMinutes(-2)));
        await files.RawSnapshotAsync("a", start.AddMinutes(-1), "{broken-json");
        await files.RawSnapshotAsync("a", start, "null");
        await files.RawSnapshotAsync("a", start.AddMinutes(1), JsonSerializer.Serialize(files.Snapshot(start.AddMinutes(1)) with { AccountId = "other" }));
        await files.RawSnapshotAsync("a", start.AddMinutes(2), JsonSerializer.Serialize(files.Snapshot(start.AddMinutes(2)) with { Windows = null! }));
        await files.RawSnapshotAsync("a", start.AddMinutes(3), JsonSerializer.Serialize(files.Snapshot(start.AddMinutes(4))));
        var unknown = files.Snapshot(start.AddMinutes(5)) with
        { Health = SourceHealth.Stale, Windows = [new("monthly", "Monthly", UsageUnit.Credits, 5, null, null)] };
        await files.Store.RecordSnapshotAsync(unknown);
        var values = await files.Store.HistoryAsync("a", start, files.Now, true);
        Assert.Equal(6, values.Count);
        Assert.Equal(start.AddMinutes(-2), values[0].ObservedAt);
        Assert.Equal(Enumerable.Range(0, 4).Select(i => start.AddMinutes(i)), values.Skip(1).Take(4).Select(value => value.ObservedAt));
        Assert.All(values.Skip(1).Take(4), value =>
        {
            Assert.Equal("a", value.AccountId);
            Assert.Empty(value.Subject); Assert.Empty(value.Scope);
            Assert.Empty(value.Windows); Assert.Null(value.Reserve);
            Assert.Equal(SourceHealth.Stale, value.Health);
            Assert.Equal("History Unavailable", value.Message);
        });
        Assert.Null(values[^1].Windows[0].RemainingPercent);
        Assert.Equal(SourceHealth.Stale, values[^1].Health);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("window-null")]
    [InlineData("window-id-null")]
    [InlineData("window-id-empty")]
    [InlineData("credit-null")]
    [InlineData("credit-id-null")]
    [InlineData("credit-id-empty")]
    [InlineData("credit-status-null")]
    public async Task UnusableRowsBreakProjectionContinuityWithoutInventingUsageOrEvents(string malformed)
    {
        using var files = new HistoryFixture(); var start = files.Now.AddHours(-1);
        var first = files.Snapshot(start) with { Windows = [new("codex/weekly", "Weekly", UsageUnit.Percent, 40, 100, null, 10080)] };
        var last = files.Snapshot(start.AddMinutes(4)) with { Windows = [first.Windows[0] with { Used = 9 }] };
        await files.Store.RecordSnapshotAsync(first);
        var bad = files.Snapshot(start.AddMinutes(2));
        var credit = new ResetCredit("credit", "available", start.AddMinutes(3));
        bad = malformed switch
        {
            "window-null" => bad with { Windows = [null!] },
            "window-id-null" => bad with { Windows = [bad.Windows[0] with { Id = null! }] },
            "window-id-empty" => bad with { Windows = [bad.Windows[0] with { Id = " " }] },
            "credit-null" => bad with { Reserve = new(1, [null!]) },
            "credit-id-null" => bad with { Reserve = new(1, [credit with { Id = null! }]) },
            "credit-id-empty" => bad with { Reserve = new(1, [credit with { Id = " " }]) },
            "credit-status-null" => bad with { Reserve = new(1, [credit with { Status = null! }]) },
            _ => bad
        };
        // Corrupt persisted input directly without evaluating computed domain properties.
        await files.RawSnapshotAsync("a", bad.ObservedAt, malformed == "json" ? "{broken-json" :
            JsonSerializer.Serialize(bad, new JsonSerializerOptions { IgnoreReadOnlyProperties = true }));
        await files.Store.RecordSnapshotAsync(last);
        var records = await files.Store.HistoryAsync("a", start, files.Now);
        Assert.Equal(3, records.Count);
        Assert.Equal(SourceHealth.Stale, records[1].Health);
        Assert.Empty(records[1].Windows);
        var series = UsageHistory.Build(files.Account, records, start, files.Now);
        Assert.Equal(new decimal[] { 60, 91 }, series.Points.Select(point => point.RemainingPercent));
        Assert.True(series.Points[1].StartsSegment);
        Assert.Null(series.Points[1].PreviousObservedAt);
        Assert.Null(series.Points[1].ReplenishedFrom);
        Assert.Empty(series.Events);
    }

    [Fact]
    public async Task DuplicateTimestampsReplaceAndCancellationDoesNotLockLaterQueries()
    {
        using var files = new HistoryFixture(); var sample = files.Snapshot(files.Now);
        await files.Store.RecordSnapshotAsync(sample);
        await files.Store.RecordSnapshotAsync(sample with { PlanType = "pro" });
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.Store.HistoryAsync("a", files.Now, files.Now, ct: cancellation.Token));
        Assert.Equal("pro", Assert.Single(await files.Store.HistoryAsync("a", files.Now, files.Now)).PlanType);
        await Assert.ThrowsAsync<ArgumentException>(() => files.Store.HistoryAsync("a", files.Now, files.Now.AddTicks(-1)));
    }

    [Fact]
    public async Task RangeAndPrecedingReadPreserveRetentionAndRemovalCleanup()
    {
        using var files = new HistoryFixture();
        await files.Store.RecordSnapshotAsync(files.Snapshot(files.Now.AddDays(-45)));
        await files.Store.RecordSnapshotAsync(files.Snapshot(files.Now.AddDays(-44)));
        files.Clock.Now = files.Now.AddDays(2);
        Assert.Empty(await files.Store.HistoryAsync("a", files.Now.AddDays(-1), files.Clock.Now, true));
        await files.Store.RecordSnapshotAsync(files.Snapshot(files.Clock.Now));
        await files.Store.DeleteHistoryAsync("a");
        Assert.Empty(await files.Store.HistoryAsync("a", files.Now, files.Clock.Now, true));
    }
}

[Collection("App orchestration")]
public sealed class InitialHistoryPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedConnectionAndInitialHistorySurviveRestart(bool reconnect)
    {
        using var files = new HistoryFixture(); var old = files.Account;
        var incoming = old with { CredentialId = "new-credential" };
        var before = files.Snapshot(files.Now.AddMinutes(-5));
        await files.Store.WriteMonitoringStateAsync(reconnect ? [old] : [], reconnect ? [before] : [], [],
            historySnapshot: reconnect ? before : null);
        var page = new AIResetRadar.App.MainPage(files.Store, new(), reconnect ? old : null, null, false);
        if (reconnect) page.SetSnapshotForTest(before);
        var sample = files.Snapshot(files.Now);
        await page.CommitForTestAsync(incoming, sample, reconnect ? old : null);
        await page.WaitForCleanupAsync();
        var restarted = new LocalStore(files.Database, files.Clock);
        Assert.Equal(incoming.CredentialId, Assert.Single((await restarted.ReadAsync<ProviderAccount[]>("accounts"))!).CredentialId);
        Assert.Equal(sample.ObservedAt, Assert.Single((await restarted.ReadAsync<QuotaSnapshot[]>("snapshots"))!).ObservedAt);
        var history = await restarted.HistoryAsync(old.Id, before.ObservedAt, files.Now);
        Assert.Equal(reconnect ? 2 : 1, history.Count);
        Assert.Equal(sample.ObservedAt, history[^1].ObservedAt);
    }

    [Fact]
    public async Task HistoryInsertFailureRollsBackConnectionSnapshotRetirementAndAlerts()
    {
        using var files = new HistoryFixture(); var original = files.Account;
        var before = files.Snapshot(files.Now.AddMinutes(-5));
        await files.Store.WriteMonitoringStateAsync([original], [before], [], [], historySnapshot: before);
        var page = new AIResetRadar.App.MainPage(files.Store, new(), original, null, false);
        page.SetSnapshotForTest(before);
        await files.ExecuteAsync("CREATE TRIGGER reject_history BEFORE INSERT ON history BEGIN SELECT RAISE(ABORT,'fixture history failure'); END;");
        var current = files.Snapshot(files.Now) with { Windows = [new("weekly", "Weekly", UsageUnit.Percent, 95, 100, files.Now.AddDays(1))] };
        var incoming = original with { CredentialId = "replacement" };
        await Assert.ThrowsAsync<SqliteException>(() => page.CommitForTestAsync(incoming, current, original));
        var restarted = new LocalStore(files.Database, files.Clock);
        Assert.Equal(original.CredentialId, Assert.Single((await restarted.ReadAsync<ProviderAccount[]>("accounts"))!).CredentialId);
        Assert.Equal(before.ObservedAt, Assert.Single((await restarted.ReadAsync<QuotaSnapshot[]>("snapshots"))!).ObservedAt);
        Assert.Empty((await restarted.ReadAsync<ProviderAccount[]>("retired"))!);
        Assert.Empty((await restarted.ReadAsync<AlertEvent[]>("alerts")) ?? []);
        Assert.Equal(before.ObservedAt, Assert.Single(await restarted.HistoryAsync("a", before.ObservedAt, files.Now)).ObservedAt);
        await files.ExecuteAsync("DROP TRIGGER reject_history");
        await page.CommitForTestAsync(incoming, current, original);
        await page.WaitForCleanupAsync();
        Assert.Equal(2, (await restarted.HistoryAsync("a", before.ObservedAt, files.Now)).Count);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("subject")]
    [InlineData("scope")]
    [InlineData("removed")]
    [InlineData("snapshot")]
    public async Task RejectedConnectionNeverCreatesHistory(string rejection)
    {
        using var files = new HistoryFixture(); var original = files.Account;
        await files.Store.WriteMonitoringStateAsync([original], [], []);
        var page = new AIResetRadar.App.MainPage(files.Store, new(), rejection == "removed" ? null : original, null, false);
        var incoming = rejection switch
        {
            "duplicate" => original with { Id = "duplicate-id" },
            "subject" => original with { Subject = "other-user" },
            "scope" => original with { Scope = "other-scope" },
            _ => original
        };
        var sample = files.Snapshot(files.Now) with { AccountId = incoming.Id, Subject = incoming.Subject, Scope = incoming.Scope };
        if (rejection == "snapshot") sample = sample with { Subject = "wrong-snapshot-user" };
        await Assert.ThrowsAsync<InvalidDataException>(() => page.CommitForTestAsync(incoming, sample, rejection == "duplicate" ? null : original));
        Assert.Empty(await files.Store.HistoryAsync(incoming.Id, files.Now.AddDays(-1), files.Now));
        Assert.Equal(original, Assert.Single((await files.Store.ReadAsync<ProviderAccount[]>("accounts"))!));
    }

    [Fact]
    public async Task InitialCapturePreservesOriginalObservationAgeAndHealth()
    {
        using var files = new HistoryFixture();
        var sample = files.Snapshot(files.Now.AddMinutes(-20)) with { Health = SourceHealth.Stale };
        await files.Store.WriteMonitoringStateAsync([files.Account], [sample], historySnapshot: sample);
        var saved = Assert.Single(await files.Store.HistoryAsync("a", files.Now.AddHours(-1), files.Now));
        Assert.Equal(sample.ObservedAt, saved.ObservedAt);
        Assert.Equal(SourceHealth.Stale, saved.Health);
        await Assert.ThrowsAsync<InvalidDataException>(() => files.Store.WriteMonitoringStateAsync([files.Account], [], historySnapshot: sample));
        Assert.Single(await files.Store.HistoryAsync("a", files.Now.AddHours(-1), files.Now));
    }
}

sealed class HistoryFixture : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "AIResetRadar-tests", Guid.NewGuid().ToString("N"));
    public string Database => Path.Combine(folder, "radar.db");
    public DateTimeOffset Now { get; } = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    public HistoryClock Clock { get; }
    public LocalStore Store { get; }
    public ProviderAccount Account { get; } = new("a", "OpenAI", "Personal", "sample-user", "scope", CredentialId: "original-credential");
    public HistoryFixture() { Clock = new(Now); Store = new(Database, Clock); }
    public QuotaSnapshot Snapshot(DateTimeOffset at) => new(Account.Id, Account.Subject, Account.Scope, at,
        [new("codex/weekly", "Weekly", UsageUnit.Percent, 20, 100, null, 10080)], null);
    public async Task<SqliteConnection> OpenAsync()
    { var connection = new SqliteConnection($"Data Source={Database}"); await connection.OpenAsync(); return connection; }
    public async Task ExecuteAsync(string sql)
    { await using var connection = await OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
    public async Task RawSnapshotAsync(string account, DateTimeOffset at, string json)
    {
        await using var connection = await OpenAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO history(account_id,observed_at,value) VALUES($account,$at,$json)";
        command.Parameters.AddWithValue("$account", account); command.Parameters.AddWithValue("$at", at.ToUnixTimeMilliseconds()); command.Parameters.AddWithValue("$json", json);
        await command.ExecuteNonQueryAsync();
    }
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AIResetRadar-tests")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(root, Path.GetFullPath(folder));
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}

sealed class HistoryClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
