using AIResetRadar.Core;
using AIResetRadar.App.Platforms.Windows;
using Microsoft.Data.Sqlite;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;

[Collection("App orchestration")]
public sealed class PreferencePersistenceTests
{
    [Fact] public async Task ConcurrentThresholdDragsPreserveOtherBarsAndMutedChoices()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, false);
        await Task.WhenAll(page.SetThresholdsForTestAsync("a", "short", 40, 15),
            page.SetThresholdsForTestAsync("a", "weekly", 35, 12), page.SetCapacityAlertsForTestAsync("a", "weekly", false));
        var saved = (await new LocalStore(files.Path).ReadAsync<RadarSettings>("settings"))!;
        Assert.Equal(40, saved.RuleFor("a", "short").LowThreshold);
        Assert.Equal(15, saved.RuleFor("a", "short").CriticalThreshold);
        Assert.Equal(35, saved.RuleFor("a", "weekly").LowThreshold);
        Assert.Equal(12, saved.RuleFor("a", "weekly").CriticalThreshold);
        Assert.Equal(30, saved.RuleFor("b", "short").LowThreshold);
        Assert.False(saved.CapacityAlertsEnabled("a", "weekly"));
    }
    [Fact] public async Task FailedThresholdSaveKeepsPreviouslyAcceptedLevels()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, false);
        await page.SetThresholdsForTestAsync("a", "short", 35, 12);
        await using (var database = new SqliteConnection($"Data Source={files.Path}"))
        {
            await database.OpenAsync(); await using var command = database.CreateCommand();
            command.CommandText = "CREATE TRIGGER reject_settings BEFORE UPDATE ON state WHEN NEW.key='settings' BEGIN SELECT RAISE(ABORT,'fixture settings failure'); END;";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<SqliteException>(() => page.SetThresholdsForTestAsync("a", "short", 50, 20));
        Assert.Equal(35, page.PreferencesForTest.RuleFor("a", "short").LowThreshold);
        Assert.Equal(12, (await store.ReadAsync<RadarSettings>("settings"))!.RuleFor("a", "short").CriticalThreshold);
    }
    [Fact] public async Task DemoDragsLeaveSavedThresholdsUntouched()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        await store.WriteAsync("settings", new RadarSettings());
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, true);
        await page.SetThresholdsForTestAsync("demo", "short", 40, 20);
        Assert.Equal(40, page.PreferencesForTest.RuleFor("demo", "short").LowThreshold);
        Assert.Empty((await store.ReadAsync<RadarSettings>("settings"))!.Rules ?? []);
    }
    [Fact] public async Task IndependentBarChoicesSurviveConcurrentChangesAndOtherSettingsSaves()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, false);
        await Task.WhenAll(page.SetCapacityAlertsForTestAsync("a", "short", false), page.SetCapacityAlertsForTestAsync("a", "weekly", false));
        await page.SavePreferencesForTestAsync(new(LowThreshold: 44));
        var saved = (await new LocalStore(files.Path).ReadAsync<RadarSettings>("settings"))!;
        Assert.False(saved.CapacityAlertsEnabled("a", "short"));
        Assert.False(saved.CapacityAlertsEnabled("a", "weekly"));
        Assert.True(saved.CapacityAlertsEnabled("b", "weekly"));
        Assert.Equal(44, saved.LowThreshold);
        await page.SetCapacityAlertsForTestAsync("a", "short", true);
        Assert.True(page.PreferencesForTest.CapacityAlertsEnabled("a", "short"));
        Assert.False(page.PreferencesForTest.CapacityAlertsEnabled("a", "weekly"));
    }
    [Fact] public async Task FailedBarChoiceSaveKeepsThePreviouslyAcceptedChoice()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, false);
        await page.SavePreferencesForTestAsync(new());
        await using (var database = new SqliteConnection($"Data Source={files.Path}"))
        {
            await database.OpenAsync(); await using var command = database.CreateCommand();
            command.CommandText = "CREATE TRIGGER reject_settings BEFORE UPDATE ON state WHEN NEW.key='settings' BEGIN SELECT RAISE(ABORT,'fixture settings failure'); END;";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<SqliteException>(() => page.SetCapacityAlertsForTestAsync("a", "weekly", false));
        Assert.True(page.PreferencesForTest.CapacityAlertsEnabled("a", "weekly"));
        Assert.True((await store.ReadAsync<RadarSettings>("settings"))!.CapacityAlertsEnabled("a", "weekly"));
    }
    [Fact] public async Task DemoBarChoicesDoNotChangeSavedPreferences()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        await store.WriteAsync("settings", new RadarSettings());
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, true);
        await page.SetCapacityAlertsForTestAsync("a", "weekly", false);
        Assert.False(page.PreferencesForTest.CapacityAlertsEnabled("a", "weekly"));
        Assert.True((await store.ReadAsync<RadarSettings>("settings"))!.CapacityAlertsEnabled("a", "weekly"));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedStartupOrDemoSaveDoesNotWriteRegistration(bool demo)
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        await store.WriteAsync("settings", new RadarSettings());
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, demo);
        await page.SavePreferencesForTestAsync(new(LowThreshold: 44, LaunchAtSignIn: demo));
        Assert.Equal(0, StartupService.Writes);
        Assert.Equal(0, StartupService.Restores);
        Assert.Equal(44, page.PreferencesForTest.LowThreshold);
        Assert.Equal(demo ? 30 : 44, (await store.ReadAsync<RadarSettings>("settings"))!.LowThreshold);
    }
    [Fact] public async Task SuccessfulSaveAdoptsBothPreferencesAndStartup()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, false);
        await page.SavePreferencesForTestAsync(new(LaunchAtSignIn: true, LowThreshold: 44));
        Assert.Equal("new registration", StartupService.Value);
        Assert.True(page.PreferencesForTest.LaunchAtSignIn);
        Assert.True((await store.ReadAsync<RadarSettings>("settings"))!.LaunchAtSignIn);
    }
    [Fact] public async Task StartupFailureRestoresRegistrationAndDoesNotSavePreferences()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        await store.WriteAsync("settings", new RadarSettings());
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, false);
        StartupService.FailWrite = true;
        var error = await Assert.ThrowsAsync<AIResetRadar.App.Services.SettingsSaveException>(() => page.SavePreferencesForTestAsync(new(LaunchAtSignIn: true)));
        Assert.Contains("Windows startup could not be changed", error.Message);
        Assert.Equal("original registration", StartupService.Value);
        Assert.False(page.PreferencesForTest.LaunchAtSignIn);
        Assert.False((await store.ReadAsync<RadarSettings>("settings"))!.LaunchAtSignIn);
    }
    [Fact] public async Task FailedRollbackReportsThatWindowsStartupNeedsReview()
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, false);
        StartupService.FailWrite = StartupService.FailRestore = true;
        var error = await Assert.ThrowsAsync<AIResetRadar.App.Services.SettingsSaveException>(() => page.SavePreferencesForTestAsync(new(LaunchAtSignIn: true)));
        Assert.Contains("could not be restored", error.Message);
        Assert.Contains("Windows startup settings", error.Message);
        Assert.False(page.PreferencesForTest.LaunchAtSignIn);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task FailedPreferenceWriteRestoresExactStartupRegistration(bool previouslyEnabled, bool missingRegistration)
    {
        using var files = new Fixture(); var store = new LocalStore(files.Path);
        await store.WriteAsync("settings", new RadarSettings());
        var page = new AIResetRadar.App.MainPage(store, new(), null, null, false);
        if (previouslyEnabled) await page.SavePreferencesForTestAsync(new(LaunchAtSignIn: true));
        if (missingRegistration) StartupService.Value = null;
        var original = StartupService.Value;
        await using (var database = new SqliteConnection($"Data Source={files.Path}"))
        {
            await database.OpenAsync(); await using var command = database.CreateCommand();
            command.CommandText = "CREATE TRIGGER reject_settings BEFORE UPDATE ON state WHEN NEW.key='settings' BEGIN SELECT RAISE(ABORT,'fixture settings failure'); END;";
            await command.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<SqliteException>(() => page.SavePreferencesForTestAsync(new(LaunchAtSignIn: !previouslyEnabled, LowThreshold: 44)));
        Assert.Equal(original, StartupService.Value);
        Assert.Equal(1, StartupService.Restores);
        Assert.Equal(previouslyEnabled, page.PreferencesForTest.LaunchAtSignIn);
        Assert.Equal(previouslyEnabled, (await store.ReadAsync<RadarSettings>("settings"))!.LaunchAtSignIn);
    }
    sealed class Fixture : IDisposable
    {
        readonly string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AIResetRadar-tests", Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(folder, "radar.db");
        public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
