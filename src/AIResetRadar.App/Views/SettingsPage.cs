using AIResetRadar.App.Controls;
using AIResetRadar.Core;
using AIResetRadar.App.Services;
namespace AIResetRadar.App.Views;

public sealed class SettingsPage : ContentPage
{
    readonly VerticalStackLayout form = new() { Padding = new Thickness(16, 12), Spacing = 12, MaximumWidthRequest = 960, HorizontalOptions = LayoutOptions.Center };
    public SettingsPage(IReadOnlyList<ProviderAccount> accounts, IReadOnlyDictionary<string, QuotaSnapshot> snapshots, RadarSettings original, bool demo,
        Func<RadarSettings, Task> save, Func<ProviderAccount, bool, Task> update, Func<Task> history, Func<Task> test, Func<ProviderAccount, Task> reconnect, string? accountId = null, Func<string, Task>? reserves = null)
    {
        var selected = accounts.FirstOrDefault(a => a.Id == accountId);
        var pageTitle = selected is null ? "Settings" : $"{selected.Provider} / {selected.Nickname} · {PlanLabels.Display(selected.Provider, snapshots.GetValueOrDefault(selected.Id)?.PlanType)}";
        Title = pageTitle; BackgroundColor = Color.FromArgb("#141519");
        if (demo) form.Children.Add(new Label { Text = "Demo changes stay in this preview. Notification test uses Windows.", TextColor = Color.FromArgb("#B5A0F4") });
        var displayStart = form.Children.Count;
        var zones = TimeZoneInfo.GetSystemTimeZones().ToArray();
        var timeZone = new Picker { ItemsSource = zones, ItemDisplayBinding = new Binding(nameof(TimeZoneInfo.DisplayName)),
            TextColor = Color.FromArgb("#F2F1F5"), BackgroundColor = Color.FromArgb("#232429") };
        timeZone.SelectedItem = zones.FirstOrDefault(z => z.Id == TimeZones.Resolve(original.TimeZoneId).Id);
        SemanticProperties.SetDescription(timeZone, "Time Zone for dates and quiet hours");
        form.Children.Add(timeZone);
        WrapCard(displayStart, "Time Zone");
        var notificationStart = form.Children.Count;
        var notifications = Toggle("Popup notifications", original.Notifications);
        var sound = Toggle("Notification sound", original.Sound);
        var privacy = Toggle("Hide account identities in notifications", original.Privacy);
        WrapCard(notificationStart, "Notifications", "Choose how Scrooge gets your attention.");
        var quietCardStart = form.Children.Count;
        var quiet = new Switch { IsToggled = original.QuietHours, OnColor = Color.FromArgb("#8170CF"), VerticalOptions = LayoutOptions.Center };
        SemanticProperties.SetDescription(quiet, "Enable Quiet Hours");
        var quietRange = new QuietHoursRange(original.QuietStartHour, original.QuietEndHour) { IsEnabled = original.QuietHours, Opacity = original.QuietHours ? 1 : 0.5 };
        quiet.Toggled += (_, e) => { quietRange.IsEnabled = e.Value; quietRange.Opacity = e.Value ? 1 : 0.5; };
        form.Children.Add(quietRange);
        form.Children.Add(new Label { Text = "Drag a circle or focus a time and use arrow keys. One-hour steps. Monitoring continues.", FontSize = 12, TextColor = Color.FromArgb("#A5A8BA") });
        WrapCard(quietCardStart, "Quiet Hours", "A little peace, right on schedule.", quiet);
        var thresholdsStart = form.Children.Count;
        var low = PercentControl("Low capacity", original.LowThreshold);
        var critical = PercentControl("Critical capacity", original.CriticalThreshold);
        WrapCard(thresholdsStart, "Capacity alerts", "Notify me when remaining capacity drops below these levels.");
        var behaviorStart = form.Children.Count;
        var close = Toggle("Keep monitoring in the tray when window closes", original.CloseToTray);
        var startup = Toggle("Launch at Windows sign-in", original.LaunchAtSignIn);
        startup.IsEnabled = !demo;
        var showHistory = new AIResetRadar.App.Controls.ActionButton { Text = "Alert history" }; showHistory.Clicked += async (_, _) => await history(); form.Children.Add(showHistory);
        var testNotification = new AIResetRadar.App.Controls.ActionButton { Text = "Send test notification" }; testNotification.Clicked += async (_, _) => await test(); form.Children.Add(testNotification);
        WrapCard(behaviorStart, "App behavior", "Keep your capacity within reach.");
        if (accountId is not null) form.Children.Clear();
        form.Children.Add(new Label { Text = accountId is null ? "Connected accounts" : "Account settings", FontSize = 21, FontFamily = "Segoe UI Semibold", Margin = new Thickness(0, 8, 0, 0) });
        foreach (var account in accounts.Where(a => accountId is null || a.Id == accountId).ToArray())
        {
            var accountStart = form.Children.Count;
            Border? accountCard = null;
            var current = account;
            ProviderAccount Latest() => accounts.FirstOrDefault(a => a.Id == account.Id) ?? current;
            var plan = PlanLabels.Display(account.Provider, snapshots.GetValueOrDefault(account.Id)?.PlanType);
            var rename = new AIResetRadar.App.Controls.ActionButton { Text = $"{account.Provider} / {account.Nickname} · {plan} · Rename" };
            rename.Clicked += async (_, _) => await AccountAction(async () => { var name = await AIResetRadar.App.Services.ThemedDialogs.PromptAsync(this, "Account nickname", "Choose a label for this account.", Latest().Nickname); if (!string.IsNullOrWhiteSpace(name)) { current = Latest() with { Nickname = name.Trim() }; await update(current, false); rename.Text = $"{current.Provider} / {current.Nickname} · {plan} · Rename"; } });
            form.Children.Add(rename);
            var providerPages = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 10 };
            var usagePage = new AIResetRadar.App.Controls.ActionButton { Text = "Provider usage page", IsEnabled = !demo };
            var accessHelp = new AIResetRadar.App.Controls.ActionButton { Text = "Usage and access help" };
            usagePage.Clicked += async (_, _) => { if (!await AIResetRadar.App.Services.ProviderNavigation.OpenAsync(Latest())) await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Provider page unavailable", "Open the provider's usage page in your browser.", "Close"); };
            accessHelp.Clicked += async (_, _) => { if (!await AIResetRadar.App.Services.ProviderNavigation.OpenAsync(Latest(), true)) await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Help unavailable", "Open your provider's usage documentation in your browser.", "Close"); };
            providerPages.Add(usagePage); providerPages.Add(accessHelp, 1); form.Children.Add(providerPages);
            var browserNotice = new Label { Text = "Browser pages use the account signed in there. Check the account/workspace before comparing.", FontSize = 12, TextColor = Color.FromArgb("#B6B8C8") }; form.Children.Add(browserNotice);
            if (reserves is not null && snapshots.GetValueOrDefault(account.Id)?.Reserve is not null)
            {
                var credits = new AIResetRadar.App.Controls.ActionButton { Text = "Available resets" };
                credits.Clicked += async (_, _) => await reserves(account.Id);
                form.Children.Add(credits);
            }
            var rules = new AIResetRadar.App.Controls.ActionButton { Text = "Account and window alert rules" };
            rules.Clicked += async (_, _) => await Navigation.PushModalAsync(new AlertRulesPage(current, snapshots.GetValueOrDefault(current.Id)?.Windows ?? [], original,
                async updated => { var settings = original with { Rules = updated.Where(r => accounts.Any(a => a.Id == r.AccountId)).ToArray() }; await save(settings); original = settings; }));
            form.Children.Add(rules);
            var signIn = new AIResetRadar.App.Controls.ActionButton { Text = "Reconnect account", IsEnabled = !demo }; signIn.Clicked += async (_, _) => await AccountAction(() => reconnect(Latest())); form.Children.Add(signIn);
            var enabled = Toggle("Monitor this account", account.Enabled);
            var syncing = false;
            enabled.Toggled += async (_, args) => {
                if (demo || syncing) return;
                try { current = Latest() with { Enabled = args.Value }; await update(current, false); }
                catch { syncing = true; enabled.IsToggled = Latest().Enabled; syncing = false; await AccountError(); }
            };
            var remove = new AIResetRadar.App.Controls.ActionButton { Text = "Remove connection", IsEnabled = !demo };
            remove.Clicked += async (_, _) => await AccountAction(async () => { if (await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Remove connection?", "Scrooge will remove only its own saved connection for this account.", "Remove", "Cancel")) { await update(Latest(), true); if (accountId is not null) { await Navigation.PopModalAsync(); return; } if (accountCard is not null) form.Children.Remove(accountCard); } });
            form.Children.Add(remove);
            accountCard = WrapCard(accountStart, $"{account.Provider} · {plan}", account.Nickname);
        }
        var status = new Label { TextColor = Color.FromArgb("#EAAA51"), Margin = new Thickness(24, 0), HorizontalTextAlignment = TextAlignment.End };
        var saveGate = new SemaphoreSlim(1, 1);
        async Task AutoSaveAsync()
        {
            await saveGate.WaitAsync();
            try
            {
            var start = quietRange.StartHour; var end = quietRange.EndHour;
            var lowValue = (decimal)low.Value; var criticalValue = (decimal)critical.Value;
            if (quiet.IsToggled && start == end) { status.Text = "Choose different start and end times for quiet hours."; return; }
            if (criticalValue >= lowValue) { status.Text = "Critical capacity must be lower than low capacity."; return; }
            var settings = original with { Notifications = notifications.IsToggled, Sound = sound.IsToggled, Privacy = privacy.IsToggled,
                QuietHours = quiet.IsToggled, QuietStartHour = start, QuietEndHour = end, LowThreshold = lowValue, CriticalThreshold = criticalValue,
                LaunchAtSignIn = startup.IsToggled, CloseToTray = close.IsToggled,
                Rules = original.Rules?.Where(r => accounts.Any(a => a.Id == r.AccountId)).ToArray(),
                TimeZoneId = (timeZone.SelectedItem as TimeZoneInfo)?.Id ?? TimeZones.DefaultId };
            if (!settings.HasValidThresholds) { status.Text = "An account/window rule conflicts with these defaults. Adjust its thresholds first."; return; }
            try { await save(settings); original = settings; status.Text = ""; }
            catch (SettingsSaveException error) { status.Text = error.Message; }
            catch { status.Text = "Settings could not be saved. Check available space and local folder access."; }
            }
            finally { saveGate.Release(); }
        }
        timeZone.SelectedIndexChanged += async (_, _) => await AutoSaveAsync();
        foreach (var toggle in new[] { notifications, sound, privacy, quiet, close, startup })
            toggle.Toggled += async (_, _) => await AutoSaveAsync();
        low.ValueChanged += async (_, _) => await AutoSaveAsync();
        critical.ValueChanged += async (_, _) => await AutoSaveAsync();
        quietRange.Changed += async (_, _) => await AutoSaveAsync();
        async Task CloseAsync()
        {
            await saveGate.WaitAsync();
            saveGate.Release();
            if (!string.IsNullOrEmpty(status.Text)) return;
            await Navigation.PopModalAsync();
        }
        var footer = new HorizontalStackLayout { Spacing = 12, HorizontalOptions = LayoutOptions.End, Padding = new Thickness(20, 8) };
        var cancel = new AIResetRadar.App.Controls.ActionButton { Text = "Close" }; cancel.Clicked += async (_, _) => await CloseAsync(); footer.Children.Add(cancel);
        var heading = new Grid { Padding = new Thickness(20, 12), ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        heading.Add(new Label { Text = pageTitle, FontSize = 26, FontFamily = "Segoe UI Semibold", VerticalOptions = LayoutOptions.Center });
        var headerClose = new AIResetRadar.App.Controls.ActionButton { Text = "Close" }; headerClose.Clicked += async (_, _) => await CloseAsync(); heading.Add(headerClose, 1);
        var layout = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        layout.Add(heading); layout.Add(new ScrollView { Content = form }, 0, 1); layout.Add(new VerticalStackLayout { Children = { status, footer } }, 0, 2); Content = layout;
        SizeChanged += (_, _) => form.WidthRequest = Math.Max(0, Math.Min(960, Width - 24));
    }
    Switch Toggle(string title, bool value)
    {
        var toggle = new Switch { IsToggled = value, OnColor = Color.FromArgb("#8170CF") };
        SemanticProperties.SetDescription(toggle, title);
        var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 16, MinimumHeightRequest = 36 };
        row.Add(new Label { Text = title, VerticalOptions = LayoutOptions.Center }); row.Add(toggle, 1); form.Children.Add(row); return toggle;
    }
    async Task AccountAction(Func<Task> action) { try { await action(); } catch { await AccountError(); } }
    Task AccountError() => AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Account update unavailable", "Scrooge could not save this change. Check local folder access and retry.", "Close");
    Border WrapCard(int start, string title, string? description = null, View? headerAction = null)
    {
        var contents = form.Children.Skip(start).ToArray();
        var stack = new VerticalStackLayout { Spacing = 8 };
        var header = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 16 };
        header.Add(new Label { Text = title, FontSize = 21, FontFamily = "Segoe UI Semibold", TextColor = Color.FromArgb("#C3ADF8"), VerticalOptions = LayoutOptions.Center });
        if (headerAction is not null) header.Add(headerAction, 1);
        var heading = new VerticalStackLayout { Spacing = 2, Children = { header } };
        if (!string.IsNullOrEmpty(description))
            heading.Children.Add(new Label { Text = description, FontSize = 13, TextColor = Color.FromArgb("#A5A8BA") });
        stack.Children.Add(heading);
        foreach (var child in contents) { form.Children.Remove(child); stack.Children.Add(child); }
        var card = new Border { Content = stack, Padding = 16, BackgroundColor = Color.FromArgb("#1C1E24"), Stroke = Color.FromArgb("#383B46"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 } };
        form.Children.Add(card); return card;
    }
    Slider PercentControl(string title, decimal value)
    {
        var label = new Label { TextColor = Color.FromArgb("#BEC1D2") };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = (double)value,
            MinimumTrackColor = Color.FromArgb("#A990ED"), MaximumTrackColor = Color.FromArgb("#41434F"), ThumbColor = Color.FromArgb("#C3ADF8") };
        SemanticProperties.SetDescription(slider, title + ": percent remaining");
        void Update() => label.Text = $"{title} · {slider.Value:0}% remaining";
        slider.ValueChanged += (_, _) => { var rounded = Math.Round(slider.Value); if (Math.Abs(slider.Value - rounded) > 0.001) slider.Value = rounded; Update(); };
        Update(); form.Children.Add(new VerticalStackLayout { Spacing = 2, Children = { label, slider } }); return slider;
    }
}
