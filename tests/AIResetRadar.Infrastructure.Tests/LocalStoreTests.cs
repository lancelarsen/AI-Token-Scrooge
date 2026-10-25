using AIResetRadar.Core;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;

public class LocalStoreTests
{
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
        Directory.Delete(directory, true);
    }
}
