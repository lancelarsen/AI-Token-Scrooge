using AIResetRadar.Core;
namespace AIResetRadar.App.Views;

public sealed class SettingsPage : ContentPage
{
    readonly VerticalStackLayout form = new() { Padding = 32, Spacing = 16, MaximumWidthRequest = 760, HorizontalOptions = LayoutOptions.Center };
    public SettingsPage(IReadOnlyList<ProviderAccount> accounts, RadarSettings original, bool demo,
        Func<RadarSettings, Task> save, Func<ProviderAccount, bool, Task> update, Func<Task> history, Func<Task> test)
    {
        Title = "Settings"; BackgroundColor = Color.FromArgb("#141519");
        var notifications = Toggle("Popup notifications", original.Notifications);
        var sound = Toggle("Notification sound", original.Sound);
        var privacy = Toggle("Hide account identities in notifications", original.Privacy);
        var quiet = Toggle("Quiet hours", original.QuietHours);
        var quietStart = Field("Quiet hours start (0–23)", original.QuietStartHour.ToString());
        var quietEnd = Field("Quiet hours end (0–23)", original.QuietEndHour.ToString());
        var low = Field("Low capacity: percent left", original.LowThreshold.ToString());
        var critical = Field("Critical capacity: percent left", original.CriticalThreshold.ToString());
        var close = Toggle("Keep monitoring in the tray when window closes", original.CloseToTray);
        var startup = Toggle("Launch at Windows sign-in", original.LaunchAtSignIn);
        var showHistory = new Button { Text = "Alert history" }; showHistory.Clicked += async (_, _) => await history(); form.Children.Add(showHistory);
        var testNotification = new Button { Text = "Send test notification" }; testNotification.Clicked += async (_, _) => await test(); form.Children.Add(testNotification);
        form.Children.Add(new Label { Text = "Connected accounts", FontSize = 21, FontFamily = "Segoe UI Semibold", Margin = new Thickness(0, 16, 0, 0) });
        foreach (var account in accounts.ToArray())
        {
            var current = account;
            var rename = new Button { Text = $"{account.Provider} / {account.Nickname} · Rename" };
            rename.Clicked += async (_, _) => { var name = await DisplayPromptAsync("Account nickname", "Choose a label for this account.", initialValue: current.Nickname); if (!string.IsNullOrWhiteSpace(name)) { current = current with { Nickname = name.Trim() }; await update(current, false); rename.Text = $"{current.Provider} / {current.Nickname} · Rename"; } };
            form.Children.Add(rename);
            var enabled = Toggle("Monitor this account", account.Enabled);
            enabled.Toggled += async (_, args) => { if (!demo) { current = current with { Enabled = args.Value }; await update(current, false); } };
            var remove = new Button { Text = "Remove connection", IsEnabled = !demo };
            remove.Clicked += async (_, _) => { if (await DisplayAlertAsync("Remove connection?", "Radar will remove only its own saved connection for this account.", "Remove", "Cancel")) { await update(account, true); form.Children.Remove(rename); form.Children.Remove(remove); enabled.IsEnabled = false; } };
            form.Children.Add(remove);
        }
        var status = new Label { TextColor = Color.FromArgb("#EAAA51") }; form.Children.Add(status);
        var done = new Button { Text = "Save settings", BackgroundColor = Color.FromArgb("#7169CA") };
        done.Clicked += async (_, _) =>
        {
            if (!int.TryParse(quietStart.Text, out var start) || !int.TryParse(quietEnd.Text, out var end) || start is < 0 or > 23 || end is < 0 or > 23 ||
                !decimal.TryParse(low.Text, out var lowValue) || !decimal.TryParse(critical.Text, out var criticalValue) || criticalValue < 0 || lowValue > 100 || criticalValue >= lowValue)
            { status.Text = "Use valid hours and 0 ≤ critical < low ≤ 100."; return; }
            try { await save(new(notifications.IsToggled, sound.IsToggled, privacy.IsToggled, quiet.IsToggled, start, end, lowValue, criticalValue, startup.IsToggled, close.IsToggled)); await Navigation.PopModalAsync(); }
            catch { status.Text = "Settings could not be saved. Check local folder access."; }
        };
        form.Children.Add(done);
        var cancel = new Button { Text = "Close" }; cancel.Clicked += async (_, _) => await Navigation.PopModalAsync(); form.Children.Add(cancel);
        var heading = new Grid { Padding = new Thickness(24, 16), ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        heading.Add(new Label { Text = "Settings", FontSize = 26, FontFamily = "Segoe UI Semibold", VerticalOptions = LayoutOptions.Center });
        var headerClose = new Button { Text = "Close" }; headerClose.Clicked += async (_, _) => await Navigation.PopModalAsync(); heading.Add(headerClose, 1);
        var layout = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        layout.Add(heading); layout.Add(new ScrollView { Content = form }, 0, 1); Content = layout;
    }
    Switch Toggle(string title, bool value)
    {
        var toggle = new Switch { IsToggled = value, OnColor = Color.FromArgb("#8170CF") };
        var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 20 };
        row.Add(new Label { Text = title, VerticalOptions = LayoutOptions.Center }); row.Add(toggle, 1); form.Children.Add(row); return toggle;
    }
    Entry Field(string title, string value)
    {
        form.Children.Add(new Label { Text = title, TextColor = Color.FromArgb("#B6B8C8") });
        var entry = new Entry { Text = value, Keyboard = Keyboard.Numeric }; form.Children.Add(entry); return entry;
    }
}
