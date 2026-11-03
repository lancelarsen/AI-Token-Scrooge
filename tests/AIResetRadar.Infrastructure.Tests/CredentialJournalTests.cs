using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public sealed class CredentialJournalTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "ScroogeJournalTests", Guid.NewGuid().ToString("N"));
    readonly string id = Guid.NewGuid().ToString("N");
    string Database => Path.Combine(folder, "radar.db");
    LocalStore Store() => new(Database);
    ProviderAccount Account => new("account", "GitHub Copilot", "Synthetic", "fixture-user", "personal", CredentialId: id);

    [Fact]
    public async Task AcceptanceCannotLoseThePendingCredentialLocation()
    {
        var store = Store();
        await store.StageCredentialAsync("OpenAI", id, profileLocation: CredentialProfileLocation.LocalSecurityDirectory);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.WriteMonitoringStateAsync([Account with { Provider = "OpenAI" }], []));
        var restored = await Store().LoadMonitoringStateAsync();
        Assert.Empty(restored.Accounts);
        Assert.Equal(CredentialProfileLocation.LocalSecurityDirectory, Assert.Single(restored.Retired).ProfileLocation);
    }

    [Fact]
    public async Task ProtectedProfileLocationSurvivesRestartAndAcceptanceAndRaisesVersionGate()
    {
        var store = Store();
        await store.StageCredentialAsync("OpenAI", id, profileLocation: CredentialProfileLocation.LocalSecurityDirectory);
        var pending = Assert.Single((await Store().LoadMonitoringStateAsync()).Retired);
        Assert.Equal(CredentialProfileLocation.LocalSecurityDirectory, pending.ProfileLocation);
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Database}");
        await connection.OpenAsync(); await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version";
        Assert.Equal(4L, await command.ExecuteScalarAsync());
        var account = Account with { Provider = "OpenAI", ProfileLocation = CredentialProfileLocation.LocalSecurityDirectory };
        await store.WriteMonitoringStateAsync([account], []);
        Assert.Equal(account, Assert.Single((await Store().LoadMonitoringStateAsync()).Accounts));
        Assert.Empty((await Store().LoadMonitoringStateAsync()).Retired);
    }

    [Fact]
    public async Task PendingProfileLocationCannotBeRedirectedByRestaging()
    {
        var store = Store();
        await store.StageCredentialAsync("Claude", id, profileLocation: CredentialProfileLocation.LocalSecurityDirectory);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StageCredentialAsync("Claude", id));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StageCredentialAsync("Claude", Guid.NewGuid().ToString("N"), profileLocation: (CredentialProfileLocation)99));
        Assert.Equal(CredentialProfileLocation.LocalSecurityDirectory, Assert.Single((await Store().LoadMonitoringStateAsync()).Retired).ProfileLocation);
    }

    [Fact]
    public async Task InterruptedSignInIsRecoveredAsRetiredWithoutSecretsOrAccountIdentity()
    {
        await Store().StageCredentialAsync("Claude", id);
        var restarted = await Store().LoadMonitoringStateAsync();
        var abandoned = Assert.Single(restarted.Retired);
        Assert.Equal("Claude", abandoned.Provider);
        Assert.Equal(id, abandoned.CredentialId);
        Assert.Empty(abandoned.Subject); Assert.Empty(abandoned.Scope); Assert.Empty(abandoned.Nickname);
        Assert.Empty(restarted.Accounts);
        Assert.Single((await Store().ReadAsync<PendingCredential[]>("pending-credentials"))!);
    }

    [Fact]
    public async Task AcceptingConnectionAtomicallyRemovesOnlyItsPendingIntent()
    {
        var store = Store(); var other = Guid.NewGuid().ToString("N");
        await store.StageCredentialAsync("GitHub Copilot", id);
        await store.StageCredentialAsync("Claude", other);
        var snapshot = new QuotaSnapshot(Account.Id, Account.Subject, Account.Scope, DateTimeOffset.UtcNow, [], null);
        await store.WriteMonitoringStateAsync([Account], [snapshot], historySnapshot: snapshot);
        var restored = await Store().LoadMonitoringStateAsync();
        Assert.Equal(Account, Assert.Single(restored.Accounts));
        Assert.Equal(other, Assert.Single(restored.Retired).CredentialId);
        Assert.Equal(other, Assert.Single((await store.ReadAsync<PendingCredential[]>("pending-credentials"))!).CredentialId);
        Assert.Single(await store.HistoryAsync(Account.Id));
    }

    [Fact]
    public async Task CanceledCommitKeepsPendingCleanupAndDoesNotAcceptAccount()
    {
        var store = Store(); await store.StageCredentialAsync("GitHub Copilot", id);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.WriteMonitoringStateAsync([Account], [], ct: canceled.Token));
        var restored = await Store().LoadMonitoringStateAsync();
        Assert.Empty(restored.Accounts);
        Assert.Equal(id, Assert.Single(restored.Retired).CredentialId);
    }

    [Fact]
    public async Task FailedAcceptanceTransactionKeepsJournalAndPreviousAccountState()
    {
        var store = Store(); await store.StageCredentialAsync("GitHub Copilot", id);
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Database}"))
        {
            await connection.OpenAsync(); await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER reject_pending_update BEFORE UPDATE ON state WHEN NEW.key='pending-credentials' BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => store.WriteMonitoringStateAsync([Account], []));
        var restored = await Store().LoadMonitoringStateAsync();
        Assert.Empty(restored.Accounts);
        Assert.Equal(id, Assert.Single(restored.Retired).CredentialId);
    }

    [Fact]
    public async Task CleanupCompletionIsIdempotentAndPreservesOtherPendingConnections()
    {
        var store = Store(); var other = Guid.NewGuid().ToString("N");
        await store.StageCredentialAsync("OpenAI", id);
        await store.StageCredentialAsync("Claude", other);
        await store.CompleteCredentialCleanupAsync(id);
        await store.CompleteCredentialCleanupAsync(id);
        Assert.Equal(other, Assert.Single((await Store().LoadMonitoringStateAsync()).Retired).CredentialId);
    }

    [Fact]
    public async Task ActiveCredentialCannotBeStagedAndNeverAppearsAsAbandoned()
    {
        var store = Store(); await store.WriteMonitoringStateAsync([Account], []);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.StageCredentialAsync("GitHub Copilot", id));
        // Even a stale marker left by an older implementation must not retire an accepted connection.
        await store.WriteAsync("pending-credentials", new[] { new PendingCredential("GitHub Copilot", id) });
        Assert.Empty((await Store().LoadMonitoringStateAsync()).Retired);
    }

    [Fact]
    public async Task InvalidOrChangedProviderCannotRedirectCleanup()
    {
        var store = Store();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StageCredentialAsync("Unknown", id));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StageCredentialAsync("Claude", "../outside"));
        await store.StageCredentialAsync("Claude", id);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StageCredentialAsync("OpenAI", id));
        Assert.Equal("Claude", Assert.Single((await Store().LoadMonitoringStateAsync()).Retired).Provider);
    }

    public void Dispose()
    {
        TestDatabasePools.Clear(Database);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}
