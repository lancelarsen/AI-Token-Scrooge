namespace AIResetRadar.App.Controls;

// Shared help copy for global and account settings; uses the app's immediate themed hover.
internal static class SettingsHelp
{
    public static void Attach(View view, string text) => Platforms.Windows.ImmediateToolTip.Attach(view, text);

    public static string? For(string title) => title switch
    {
        "Popup Notifications" => "Show popup alerts when an enabled alert rule is triggered",
        "Alert on Icon" => "Show alert status on the app icon, even when popup notifications are off",
        "Notification Sounds" => "Play the selected sounds for alerts and notification actions",
        "Monitor This Account" => "Pause automatic usage checks and alerts when off. Your saved connection stays in place.",
        "Capacity Exhausted" => "Alert when a limit has no capacity remaining. The row's alarm must also be on.",
        "Capacity Restored" => "Alert when capacity becomes available again after being exhausted. The row's alarm must also be on.",
        "Reset Expiry Reminders" => "Remind you before earned resets expire, using the schedule in global Settings. Row alarm settings still apply.",
        "Keep Monitoring When Window Closes" => "Keep checking usage in the system tray after closing the window. Use Exit from the tray menu to stop Scrooge.",
        "Launch at Windows Sign-In" => "Start Scrooge automatically when you sign in to Windows",
        "Time Zone" => "Use this time zone for displayed dates, reset times, and quiet hours",
        "Quiet Hours" => "Keep monitoring while notification delivery is quiet during the selected hours",
        "Default Alert Levels" => "Default percentages of capacity remaining. Adjust individual limits directly on their dashboard bars.",
        "Earned Reset Expiration Alerts" => "Reminders before an earned reset expires. Expiration does not refresh your quota.",
        "Quota Refresh Alerts" => "Reminders before a scheduled quota refresh, separate from earned reset expirations",
        "Available Resets" => "Unused earned resets reported by this provider, with their expiration times when available",
        _ => null
    };
}
