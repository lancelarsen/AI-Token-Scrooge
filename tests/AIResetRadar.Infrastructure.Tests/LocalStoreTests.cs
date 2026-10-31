using AIResetRadar.Core;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;

public class LocalStoreTests
{
    sealed class Clock(DateTimeOffset now) : TimeProvider
    { public DateTimeOffset Now = now; public override DateTimeOffset GetUtcNow() => Now; }
    [Fact] public async Task HistoryExpiresAfterOfflineTimeWithoutAnotherProviderSnapshot()
    {
        using var files = new TestDirectory(); var clock = new Clock(DateTimeOffset.UtcNow);
        var store = new LocalStore(files.Database, clock);
        await store.RecordSnapshotAsync(new("a", "sample", "scope", clock.Now, [], null));
        var alert = new AlertEvent("event", "a", "weekly", "Low", "Fixture", clock.Now);
        await store.WriteAsync("alerts", new[] { alert });
        clock.Now = clock.Now.AddDays(46);
        Assert.Empty(await store.HistoryAsync("a"));
        Assert.Empty((await store.ReadAsync<AlertEvent[]>("alerts"))!);
        // A stale in-memory ledger must not restore expired events after pruning.
        await store.WriteAsync("alerts", new[] { alert });
        Assert.Empty((await store.ReadAsync<AlertEvent[]>("alerts"))!);
    }
    sealed class TestDirectory : IDisposable
    {
        readonly string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AIResetRadar-tests"));
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "AIResetRadar-tests", Guid.NewGuid().ToString("N"));
        public string Database => Path.Combine(Folder, "radar.db");
        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Assert.StartsWith(root + Path.DirectorySeparatorChar, Path.GetFullPath(Folder));
            if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
        }
    }
    [Fact] public async Task AccountSnapshotAndRetirementReferencesRollbackTogether()
    {
        using var files = new TestDirectory(); var store = new LocalStore(files.Database);
        var original = new ProviderAccount("a", "OpenAI", "Work", "sample", "scope", CredentialId: "old");
        var snapshot = new QuotaSnapshot("a", "sample", "scope", DateTimeOffset.UtcNow, [], null);
        await store.WriteMonitoringStateAsync([original], [snapshot], []);
        await using (var database = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={files.Database}"))
        {
            await database.OpenAsync(); await using var command = database.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_snapshot BEFORE UPDATE ON state WHEN NEW.key='snapshots' BEGIN SELECT RAISE(ABORT,'fixture failure'); END;";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => store.WriteMonitoringStateAsync([original with { CredentialId = "new" }], [], [original]));
        var restarted = new LocalStore(files.Database);
        Assert.Equal("old", Assert.Single((await restarted.ReadAsync<ProviderAccount[]>("accounts"))!).CredentialId);
        Assert.Single((await restarted.ReadAsync<QuotaSnapshot[]>("snapshots"))!);
        Assert.Empty((await restarted.ReadAsync<ProviderAccount[]>("retired"))!);
    }

    [Fact] public async Task LegacySchemaUpgradesWithoutChangingAccountIdentity()
    {
        using var files = new TestDirectory(); Directory.CreateDirectory(files.Folder);
        SQLitePCL.Batteries_V2.Init();
        await using (var database = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={files.Database}"))
        {
            await database.OpenAsync(); await using var command = database.CreateCommand();
            command.CommandText = "CREATE TABLE state(key TEXT PRIMARY KEY,value TEXT NOT NULL); PRAGMA user_version=1; INSERT INTO state(key,value) VALUES('accounts',$value);";
            command.Parameters.AddWithValue("$value", "[{\"Id\":\"a\",\"Provider\":\"OpenAI\",\"Nickname\":\"Work\",\"Subject\":\"sample\",\"Scope\":\"scope\",\"Enabled\":false}]");
            await command.ExecuteNonQueryAsync();
        }
        var store = new LocalStore(files.Database); var account = Assert.Single((await store.ReadAsync<ProviderAccount[]>("accounts"))!);
        Assert.Equal("a", account.Id); Assert.Equal("sample", account.Subject); Assert.False(account.Enabled); Assert.Null(account.CredentialId);
        Assert.Empty(await store.HistoryAsync(account.Id));
        var settings = new RadarSettings(Rules: [new("a", "weekly", LowThreshold: 40, Low: false)]);
        await store.WriteAsync("settings", settings);
        var rule = (await new LocalStore(files.Database).ReadAsync<RadarSettings>("settings"))!.RuleFor("a", "weekly");
        Assert.Equal(40, rule.LowThreshold); Assert.False(rule.Low);
    }

    [Fact] public async Task FutureDatabaseVersionIsRejectedWithoutDowngradingIt()
    {
        using var files = new TestDirectory(); Directory.CreateDirectory(files.Folder); SQLitePCL.Batteries_V2.Init();
        await using var database = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={files.Database}"); await database.OpenAsync();
        await using var command = database.CreateCommand(); command.CommandText = "PRAGMA user_version=99"; await command.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new LocalStore(files.Database).WriteAsync("accounts", Array.Empty<ProviderAccount>()));
        command.CommandText = "PRAGMA user_version"; Assert.Equal(99L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }
    [Fact] public async Task AccountsAndPartialSnapshotsSurviveRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AIResetRadar-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "radar.db");
        var account = new ProviderAccount("a", "OpenAI", "Personal", "sample@example.invalid", "workspace");
        var snapshot = new QuotaSnapshot(account.Id, account.Subject, account.Scope, DateTimeOffset.UtcNow,
            [new("codex/primary", "Codex", UsageUnit.Percent, 38, 100, null)], new(2, null));
        var store = new LocalStore(path);
        await store.WriteAsync("accounts", new[] { account });
        await store.WriteAsync("snapshots", new[] { snapshot });
        var restarted = new LocalStore(path);
        Assert.Equal(account, Assert.Single((await restarted.ReadAsync<ProviderAccount[]>("accounts"))!));
        var loaded = Assert.Single((await restarted.ReadAsync<QuotaSnapshot[]>("snapshots"))!);
        Assert.Equal(62, loaded.Windows[0].RemainingPercent);
        Assert.True(loaded.Reserve!.HasPartialDetails);
        // Pool clearing releases SQLite's file handles before scoped test cleanup.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AIResetRadar-tests")) + Path.DirectorySeparatorChar, Path.GetFullPath(directory));
        Directory.Delete(directory, true);
    }

    [Fact] public async Task HistoryRetainsFortyFiveDaysAndKeepsAccountScopesSeparate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AIResetRadar-tests", Guid.NewGuid().ToString("N"));
        var store = new LocalStore(Path.Combine(directory, "radar.db"));
        var now = DateTimeOffset.UtcNow;
        var old = new QuotaSnapshot("a", "sample", "workspace", now.AddDays(-46), [], null);
        await store.RecordSnapshotAsync(old);
        await store.RecordSnapshotAsync(old with { AccountId = "b", ObservedAt = now });
        await store.RecordSnapshotAsync(old with { ObservedAt = now });
        await store.RecordSnapshotAsync(old with { ObservedAt = now.AddDays(-44) });
        Assert.Equal(new[] { now, now.AddDays(-44) }.Order(), (await store.HistoryAsync("a")).Select(s => s.ObservedAt).Order());
        Assert.Single(await store.HistoryAsync("b"));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AIResetRadar-tests")) + Path.DirectorySeparatorChar, Path.GetFullPath(directory));
        Directory.Delete(directory, true);
    }

    [Fact] public async Task WindowsCredentialsRoundTripWithoutStoringPlainText()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "AIResetRadar-tests", Guid.NewGuid().ToString("N"));
        var store = new WindowsSecretStore(directory); var id = Guid.NewGuid().ToString("N");
        const string value = "fixture-only-not-a-real-token";
        await store.SaveAsync(id, value);
        Assert.Equal(value, await store.ReadAsync(id));
        Assert.DoesNotContain(value, System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, id + ".bin"))));
        store.Remove(id); Assert.Null(await store.ReadAsync(id));
        var secretStore = (ISecretStore)store;
        Assert.Throws<ArgumentException>(() => secretStore.Remove("../other-account"));
        Assert.StartsWith(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "AIResetRadar-tests")) + Path.DirectorySeparatorChar, Path.GetFullPath(directory));
        Directory.Delete(directory, true);
    }
}
