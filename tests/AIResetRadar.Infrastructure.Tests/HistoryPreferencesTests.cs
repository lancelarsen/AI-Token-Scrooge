using System.Text.Json;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class HistoryPreferencesTests
{
    [Fact]
    public void HistoryAndNewAccountsAreVisibleByDefault()
    {
        var preferences = new HistoryPreferences();
        Assert.True(preferences.Enabled);
        Assert.True(preferences.Includes("openai-plus"));
        Assert.True(preferences.Includes("openai-pro"));
        Assert.True(JsonSerializer.Deserialize<HistoryPreferences>("{}")!.Enabled);
        var newlyConnected = preferences.WithAccount("openai-plus", false).ForAccounts(["openai-plus", "claude-new"]);
        Assert.False(newlyConnected.Includes("openai-plus"));
        Assert.True(newlyConnected.Includes("claude-new"));
    }

    [Fact]
    public void AccountSelectionsAreIndependentAndCanAllBeHidden()
    {
        var initial = new HistoryPreferences();
        var choices = initial.WithAccount("plus", false).WithAccount("pro", false).WithAccount("copilot", false);
        Assert.False(choices.Includes("plus"));
        Assert.False(choices.Includes("pro"));
        Assert.False(choices.Includes("copilot"));
        var restored = choices.WithAccount("pro", true);
        Assert.True(restored.Includes("pro"));
        Assert.False(restored.Includes("plus"));
        Assert.False(restored.Includes("copilot"));
        Assert.True(initial.Includes("plus"));
        Assert.True(restored.Enabled);
    }

    [Fact]
    public void JsonRoundTripPreservesModeAndAccountChoices()
    {
        var original = new HistoryPreferences(false).WithAccount("plus", false).WithAccount("pro", false);
        var restored = JsonSerializer.Deserialize<HistoryPreferences>(JsonSerializer.Serialize(original))!;
        Assert.False(restored.Enabled);
        Assert.False(restored.Includes("plus"));
        Assert.False(restored.Includes("pro"));
        Assert.True(restored.Includes("claude"));
        Assert.Equal(original.HiddenAccounts, restored.HiddenAccounts);
    }

    [Fact]
    public void RemovedAccountIdsAndDuplicatesAreCleanedWithoutChangingSurvivors()
    {
        var choices = new HistoryPreferences(false, ["gone", "plus", "plus", "pro"]);
        var cleaned = choices.ForAccounts(["plus", "claude"]);
        Assert.Equal(new[] { "plus" }, cleaned.HiddenAccounts);
        Assert.False(cleaned.Enabled);
        Assert.False(cleaned.Includes("plus"));
        Assert.True(cleaned.Includes("claude"));
        Assert.True(cleaned.Includes("gone"));
    }

    [Fact]
    public async Task StoreRestartPreservesVisibilityWithoutChangingMonitoringOrAlertPreferences()
    {
        using var files = new TestDirectory();
        var store = new LocalStore(files.Database);
        var account = new ProviderAccount("plus", "OpenAI", "Personal", "subject", "scope");
        var snapshot = new QuotaSnapshot(account.Id, account.Subject, account.Scope, DateTimeOffset.UtcNow, [], null);
        var settings = new RadarSettings(Notifications: true, AlertOnIcon: true,
            Rules: [new(account.Id, "weekly", LowThreshold: 35, CriticalThreshold: 15, InfoThreshold: 55)],
            DisabledCapacityAlerts: [new(account.Id, "five-hour")]);
        await store.WriteMonitoringStateAsync([account], [snapshot], []);
        await store.WriteAsync("settings", settings);
        await store.WriteAsync("history-view", new HistoryPreferences(false).WithAccount(account.Id, false));

        var reopened = new LocalStore(files.Database);
        var choices = (await reopened.ReadAsync<HistoryPreferences>("history-view"))!;
        Assert.False(choices.Enabled);
        Assert.False(choices.Includes(account.Id));
        Assert.Equal(JsonSerializer.Serialize(settings), JsonSerializer.Serialize(await reopened.ReadAsync<RadarSettings>("settings")));
        Assert.True(Assert.Single((await reopened.ReadAsync<ProviderAccount[]>("accounts"))!).Enabled);
        Assert.Equal(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(Assert.Single((await reopened.ReadAsync<QuotaSnapshot[]>("snapshots"))!)));
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
}
