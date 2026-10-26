using AIResetRadar.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

[Collection("App orchestration")]
public class DisableAccountTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisableIsAccountScopedAndOnlyDismissesBannerAfterCommit(bool rejectWrite)
    {
        var folder = Path.Combine(Path.GetTempPath(), "AIResetRadar-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(folder, "radar.db");
            var store = new LocalStore(path);
            var first = new ProviderAccount("first", "OpenAI", "Pro", "first-subject", "personal");
            var second = new ProviderAccount("second", "OpenAI", "Plus", "second-subject", "personal");
            await store.WriteMonitoringStateAsync([first, second], []);
            var monitor = new AIResetRadar.App.Services.TestCredentialMonitor();
            var page = new AIResetRadar.App.MainPage(store, monitor, first, null, false);
            page.AddAccountForTest(second);
            using var firstRead = new CancellationTokenSource();
            using var secondRead = new CancellationTokenSource();
            page.TrackReadForTest(first.Id, firstRead);
            page.TrackReadForTest(second.Id, secondRead);
            if (rejectWrite)
            {
                await using var db = new SqliteConnection($"Data Source={path}");
                await db.OpenAsync();
                using var command = db.CreateCommand();
                command.CommandText = "CREATE TRIGGER reject_state BEFORE INSERT ON state BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;";
                await command.ExecuteNonQueryAsync();
                await Assert.ThrowsAsync<SqliteException>(() => page.EditForTestAsync(first with { Enabled = false }));
            }
            else await page.EditForTestAsync(first with { Enabled = false });
            await page.WaitForCleanupAsync();
            var saved = (await store.ReadAsync<ProviderAccount[]>("accounts"))!;
            Assert.Equal(rejectWrite, saved.Single(a => a.Id == first.Id).Enabled);
            Assert.Equal(second, saved.Single(a => a.Id == second.Id));
            Assert.True(firstRead.IsCancellationRequested);
            Assert.False(secondRead.IsCancellationRequested);
            Assert.Empty(monitor.Disconnected);
            Assert.Equal(rejectWrite ? Array.Empty<string>() : new[] { first.Id }, page.DismissedBannersForTest);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
}
