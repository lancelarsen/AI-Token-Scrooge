using Microsoft.Win32;
namespace AIResetRadar.App.Platforms.Windows;
public static class StartupService
{
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) key.SetValue("AIResetRadar", $"\"{Environment.ProcessPath}\" --tray");
        else key.DeleteValue("AIResetRadar", false);
    }
}
