using AIResetRadar.Core;
using Microsoft.Data.Sqlite;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;

[Collection("App orchestration")]
public sealed class AlertPersistenceTests
{
    [Fact] public async Task MutedBarDoesNotDeliverAPreviouslyPreparedWarning()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var account = new ProviderAccount("a", "OpenAI", "Personal", "qa-user", "personal");
        var snapshot = new QuotaSnapshot(account.Id, account.Subject, account.Scope, DateTimeOffset.UtcNow, [], null);
        AlertEvent[] alerts = [new("a/weekly/1/critical", "a", "weekly", "Critical", "Fixture", DateTimeOffset.UtcNow),
            new("a/short/1/low", "a", "short", "Low", "Fixture", DateTimeOffset.UtcNow)];
        await store.WriteAsync("alerts", alerts);
        var page = new AIResetRadar.App.MainPage(store, new(), account, null, false);
        await page.SetCapacityAlertsForTestAsync("a", "weekly", false);
        await page.DeliverRecordedForTestAsync(account, snapshot, alerts);
        Assert.Equal("short", Assert.Single(AIResetRadar.App.Platforms.Windows.NotificationService.Current.Attempts).WindowId);
    }
    [Fact] public async Task DeliveryStatusFailureDoesNotSkipOtherCommittedWarnings()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var account = new ProviderAccount("saved", "GitHub Copilot", "Saved", "qa-user", "personal");
        var current = new QuotaSnapshot(account.Id, account.Subject, account.Scope, DateTimeOffset.UtcNow,
            [new("first", "First", UsageUnit.Percent, 80, 100, DateTimeOffset.UtcNow.AddDays(2)),
             new("second", "Second", UsageUnit.Percent, 90, 100, DateTimeOffset.UtcNow.AddDays(3))], null);
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, false);
        var notifications = AIResetRadar.App.Platforms.Windows.NotificationService.Current;
        notifications.OnShow = _ =>
        {
            if (notifications.Attempts.Count != 1) return;
            using var connection = new SqliteConnection($"Data Source={files.Path}"); connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER reject_delivery BEFORE UPDATE ON state WHEN NEW.key='alerts' BEGIN SELECT RAISE(ABORT,'fixture delivery status failure'); END;";
            command.ExecuteNonQuery();
        };
        await page.CommitForTestAsync(account, current, null);
        await page.WaitForCleanupAsync();
        Assert.Equal(4, notifications.Attempts.Count);
        Assert.Equal(4, (await store.ReadAsync<AlertEvent[]>("alerts"))!.Length);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConnectionAndItsInitialWarningCommitTogether(bool reconnect)
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var old = new ProviderAccount("saved", "GitHub Copilot", "Saved", "qa-user", "personal", CredentialId: "11111111111111111111111111111111");
        var incoming = old with { CredentialId = "22222222222222222222222222222222" };
        var previous = new QuotaSnapshot(old.Id, old.Subject, old.Scope, DateTimeOffset.UtcNow,
            [new("weekly", "Weekly", UsageUnit.Percent, 10, 100, DateTimeOffset.UtcNow.AddDays(2))], null);
        var current = previous with { Windows = [previous.Windows[0] with { Used = 80 }] };
        await store.WriteMonitoringStateAsync(reconnect ? [old] : [], reconnect ? [previous] : [], []);
        var page = new AIResetRadar.App.MainPage(store, new(), reconnect ? old : null, null, false);
        if (reconnect) page.SetSnapshotForTest(previous);
        await Execute(files.Path, "CREATE TRIGGER reject_initial_alert BEFORE INSERT ON state WHEN NEW.key='alerts' BEGIN SELECT RAISE(ABORT,'fixture write failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => page.CommitForTestAsync(incoming, current, reconnect ? old : null));
        var accounts = (await store.ReadAsync<ProviderAccount[]>("accounts"))!;
        Assert.Equal(reconnect ? 1 : 0, accounts.Length);
        if (reconnect) Assert.Equal(old.CredentialId, accounts[0].CredentialId);
        await Execute(files.Path, "DROP TRIGGER reject_initial_alert");
        await page.CommitForTestAsync(incoming, current, reconnect ? old : null);
        await page.WaitForCleanupAsync();
        Assert.Equal(incoming.CredentialId, (await store.ReadAsync<ProviderAccount[]>("accounts"))!.Single().CredentialId);
        var committed = (await store.ReadAsync<AlertEvent[]>("alerts"))!;
        Assert.Equal(2, committed.Length);
        Assert.Single(committed, a => a.IsRefreshReminder);
        Assert.Single(committed, a => a.Key.EndsWith("/low"));
    }
    [Fact] public async Task IntentIsDeduplicatedDurablyAndDeliveryUpdatePreservesOtherEvents()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        await store.WriteAsync("alerts", Array.Empty<AlertEvent>());
        var first = new AlertEvent("a/weekly/low", "a", "weekly", "Low", "Fixture", DateTimeOffset.UtcNow);
        var secondStore = new LocalStore(files.Path);
        var attempts = await Task.WhenAll(Task.Run(() => store.TryRecordAlertAsync(first)), Task.Run(() => secondStore.TryRecordAlertAsync(first)));
        Assert.Single(attempts, recorded => recorded);
        Assert.True(await store.TryRecordAlertAsync(first with { Key = "b/weekly/low", AccountId = "b" }));
        await store.RecordAlertDeliveryAsync(first.Key, true);
        await store.RecordAlertDeliveryAsync(first.Key, false);
        var reopened = new LocalStore(files.Path);
        Assert.False(await reopened.TryRecordAlertAsync(first));
        var events = (await reopened.ReadAsync<AlertEvent[]>("alerts"))!;
        Assert.Equal(2, events.Length);
        Assert.True(events.Single(e => e.Key == first.Key).Delivered);
        Assert.False(events.Single(e => e.AccountId == "b").Delivered);
    }
    [Fact] public async Task FailedIntentDoesNotSuppressRetryOrEnterTheUiHistory()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        await store.WriteAsync("alerts", Array.Empty<AlertEvent>());
        var account = new ProviderAccount("saved", "GitHub Copilot", "Saved", "qa-user", "personal");
        var page = new AIResetRadar.App.MainPage(store, new(), account, account, false);
        var alert = new AlertEvent("a/weekly/low", account.Id, "weekly", "Low", "Fixture", DateTimeOffset.UtcNow);
        await Execute(files.Path, "CREATE TRIGGER reject_alert BEFORE UPDATE ON state WHEN NEW.key='alerts' BEGIN SELECT RAISE(ABORT,'fixture write failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => page.RecordIntentForTestAsync(alert));
        Assert.Empty(page.AlertHistoryForTest);
        Assert.Empty((await store.ReadAsync<AlertEvent[]>("alerts"))!);
        await Execute(files.Path, "DROP TRIGGER reject_alert");
        Assert.True(await page.RecordIntentForTestAsync(alert));
        Assert.Single(page.AlertHistoryForTest);
        Assert.False(await page.RecordIntentForTestAsync(alert));
    }
    static async Task Execute(string path, string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
    sealed class Fixture : IDisposable
    {
        readonly string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AIResetRadar-tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(folder, "radar.db");
        public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
