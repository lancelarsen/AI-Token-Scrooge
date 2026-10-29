using Microsoft.Win32;
namespace AIResetRadar.App.Platforms.Windows;
public sealed record StartupRegistration(object? Value, RegistryValueKind Kind);
public static class StartupService
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "AIResetRadar";
    public static StartupRegistration Capture()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        var value = key?.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new(value, value is null ? RegistryValueKind.String : key!.GetValueKind(ValueName));
    }
    public static void Restore(StartupRegistration registration)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (registration.Value is null) key.DeleteValue(ValueName, false);
        else key.SetValue(ValueName, registration.Value, registration.Kind);
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --tray --data-dir \"{AIResetRadar.App.Services.AppServices.Current.DirectoryPath}\"");
        else key.DeleteValue(ValueName, false);
    }
}
