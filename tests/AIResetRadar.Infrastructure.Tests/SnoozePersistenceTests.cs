using Microsoft.Data.Sqlite;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;

[Collection("App orchestration")]
public sealed class SnoozePersistenceTests
{
    [Fact]
    public async Task FailedSaveKeepsPriorSnoozeAndAllowsRetry()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, false);
        Assert.True(await page.SnoozeForTestAsync("existing"));
        var previous = page.SnoozesForTest["existing"];
        await ExecuteAsync(files.Path, "CREATE TRIGGER reject_snooze BEFORE UPDATE ON state WHEN NEW.key='snoozed' BEGIN SELECT RAISE(ABORT,'fixture failure'); END;");
        Assert.False(await page.SnoozeForTestAsync("existing"));
        Assert.False(await page.SnoozeForTestAsync("new"));
        Assert.Equal(previous, page.SnoozesForTest["existing"]);
        Assert.False(page.SnoozesForTest.ContainsKey("new"));
        var saved = await store.ReadAsync<Dictionary<string, DateTimeOffset>>("snoozed");
        Assert.Single(saved!); Assert.Equal(previous, saved!["existing"]);
        await ExecuteAsync(files.Path, "DROP TRIGGER reject_snooze;");
        Assert.True(await page.SnoozeForTestAsync("new"));
        Assert.Equal(2, (await store.ReadAsync<Dictionary<string, DateTimeOffset>>("snoozed"))!.Count);
    }

    [Fact]
    public async Task ConcurrentSnoozesRetainBothAccounts()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, false);
        var before = DateTimeOffset.UtcNow.AddHours(1);
        Assert.All(await Task.WhenAll(page.SnoozeForTestAsync("one"), page.SnoozeForTestAsync("two")), result => Assert.True(result));
        var saved = await store.ReadAsync<Dictionary<string, DateTimeOffset>>("snoozed");
        Assert.Equal(2, saved!.Count);
        foreach (var entry in saved) Assert.Equal(page.SnoozesForTest[entry.Key], entry.Value);
        Assert.All(saved.Values, until => Assert.InRange(until, before, DateTimeOffset.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task DemoSnoozeNeverOverwritesSavedLiveSnoozes()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var original = DateTimeOffset.UtcNow.AddHours(2);
        await store.WriteAsync("snoozed", new Dictionary<string, DateTimeOffset> { ["live"] = original });
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, true);
        Assert.True(await page.SnoozeForTestAsync("demo"));
        var saved = await store.ReadAsync<Dictionary<string, DateTimeOffset>>("snoozed");
        Assert.Single(saved!); Assert.Equal(original, saved!["live"]);
        Assert.True(page.SnoozesForTest.ContainsKey("demo"));
    }

    static async Task ExecuteAsync(string path, string sql)
    {
        await using var db = new SqliteConnection($"Data Source={path}");
        await db.OpenAsync(); await using var command = db.CreateCommand();
        command.CommandText = sql; await command.ExecuteNonQueryAsync();
    }
    sealed class Fixture : IDisposable
    {
        readonly string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AIResetRadar-tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(folder, "radar.db");
        public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
