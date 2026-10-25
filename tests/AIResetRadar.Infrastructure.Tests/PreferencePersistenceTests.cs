using AIResetRadar.Core;
using AIResetRadar.App.Platforms.Windows;
using Microsoft.Data.Sqlite;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;

[Collection("App orchestration")]
public sealed class PreferencePersistenceTests
{
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
