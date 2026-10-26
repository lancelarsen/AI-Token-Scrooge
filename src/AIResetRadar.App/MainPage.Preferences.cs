using AIResetRadar.App.Platforms.Windows;
using AIResetRadar.App.Services;
using AIResetRadar.Core;
namespace AIResetRadar.App;

public sealed partial class MainPage
{
    readonly SemaphoreSlim preferenceChanges = new(1, 1);
    async Task SaveSettingsAsync(RadarSettings settings)
    {
        await preferenceChanges.WaitAsync();
        try
        {
            if (!state.IsDemo)
            {
                var startupChanged = settings.LaunchAtSignIn != preferences.LaunchAtSignIn;
                var original = startupChanged ? StartupService.Capture() : null;
                var startupApplied = false;
                try
                {
                    if (startupChanged) { StartupService.SetEnabled(settings.LaunchAtSignIn); startupApplied = true; }
                    await AppServices.Current.Store.WriteAsync("settings", settings);
                }
                catch (Exception error)
                {
                    if (original is not null)
                    {
                        try { StartupService.Restore(original); }
                        catch { throw new SettingsSaveException("Settings could not be saved, and the previous Windows startup setting could not be restored. Review Scrooge in Windows startup settings.", error); }
                    }
                    if (startupChanged && !startupApplied) throw new SettingsSaveException("Windows startup could not be changed. Settings were not saved.", error);
                    throw;
                }
            }
            preferences = settings; Render(); UpdateTray();
        }
        finally { preferenceChanges.Release(); }
    }
}
