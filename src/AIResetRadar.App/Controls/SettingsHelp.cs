namespace AIResetRadar.App.Controls;

// Shared help copy for global and account settings; uses the app's immediate themed hover.
internal static class SettingsHelp
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<View, HelpText> Help = new();
    sealed record HelpText(string Text);

    public static void Attach(View view, string text)
    {
        Help.Remove(view);
        Help.Add(view, new(text));
        Platforms.Windows.ImmediateToolTip.Attach(view, text);
    }

    public static View WithInfo(View view)
    {
        if (!Help.TryGetValue(view, out var help)) return view;
        var icon = new Label { Text = "\uE946", FontFamily = "Segoe Fluent Icons", FontSize = 16,
            TextColor = Color.FromArgb("#B5A0F4"), VerticalOptions = LayoutOptions.Center,
            WidthRequest = 20, HorizontalTextAlignment = TextAlignment.Center };
        SemanticProperties.SetDescription(icon, help.Text);
        Platforms.Windows.ImmediateToolTip.Attach(icon, help.Text);
        var row = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 6,
            HorizontalOptions = view.HorizontalOptions, VerticalOptions = view.VerticalOptions };
        row.Add(icon); row.Add(view, 1);
        return row;
    }

    public static string? For(string title) => title switch
    {
        "Popup Notifications" => "Show popup alerts when an enabled alert rule is triggered",
        "Alert on Icon" => "Show alert status on the app icon, even when popup notifications are off",
        "Notification Sounds" => "Play the selected sounds for alerts and notification actions",
        "Monitor This Account" => "Pause automatic usage checks and alerts when off. Your saved connection stays in place.",
        "Capacity Exhausted" => "Alert when a usage limit reaches 0% remaining. Enable the alarm on that limit's dashboard row to receive this alert.",
        "Capacity Restored" => "Alert when a previously exhausted limit has capacity again and the provider confirms usage is allowed. Enable the alarm on that limit's dashboard row to receive this alert.",
        "Reset Expiry Reminders" => "Remind you before an unused earned reset expires, using Earned Reset Expiration Alerts in global Settings. Expiration does not refresh your quota. At least one dashboard row alarm for this account must be on.",
        "Keep Monitoring When Window Closes" => "Keep checking usage in the system tray after closing the window. Use Exit from the tray menu to stop Scrooge.",
        "Launch at Windows Sign-In" => "Start Scrooge automatically when you sign in to Windows",
        "Notifications" => "Popup notifications show triggered alerts. Alert on Icon keeps alert status visible on the app icon, even when popups are off.",
        "Sounds" => "Choose a sound style and volume for alerts and notification actions. Preview buttons play each sound at the selected volume.",
        "App Behavior" => "Keep monitoring after closing the window by leaving Scrooge in the system tray. Use Exit in the tray menu to stop it. Launch at Windows Sign-In starts Scrooge automatically.",
        "Account" => "Name and color identify this account. Polling controls how often usage is checked. Turning monitoring off pauses checks and alerts while keeping the saved connection.",
        "Connection" => "Sign in again to renew the connection while keeping settings. Removing the connection only removes it from Scrooge; your provider account is unaffected.",
        "Time Zone" => "Use this time zone for displayed dates, reset times, and quiet hours",
        "Quiet Hours" => "Keep monitoring while notification delivery is quiet during the selected hours. Drag the start and end handles or use arrow keys in one-hour steps.",
        "Default Alert Levels" => "Alerts use capacity remaining, not used. Critical must be below Warning; Info must be at or above Warning. Adjust individual limits on their dashboard bars.",
        "Earned Reset Expiration Alerts" => "Reminders before an earned reset expires. Expiration does not refresh your quota. Each row sets severity and lead time in days or hours. Reminders sort from longest to shortest lead time.",
        "Quota Refresh Alerts" => "Reminders before a scheduled quota refresh, separate from earned reset expirations. Each row sets severity and lead time in days or hours. Reminders sort from longest to shortest lead time.",
        "Available Resets" => "Unused earned resets reported by this provider, with their expiration times when available",
        _ => null
    };
}
