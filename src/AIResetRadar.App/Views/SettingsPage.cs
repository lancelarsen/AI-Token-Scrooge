using AIResetRadar.App.Controls;
using AIResetRadar.Core;
using AIResetRadar.App.Services;
namespace AIResetRadar.App.Views;

public sealed class SettingsPage : ContentPage
{
    Func<Task>? closePanel;
    protected override bool OnBackButtonPressed() { if (closePanel is not null) _ = closePanel(); return true; }
    readonly VerticalStackLayout form = new() { Padding = new Thickness(16, 12), Spacing = 12, MaximumWidthRequest = 960, HorizontalOptions = LayoutOptions.Center };
    public SettingsPage(IReadOnlyList<ProviderAccount> accounts, IReadOnlyDictionary<string, QuotaSnapshot> snapshots, RadarSettings original, bool demo,
        Func<RadarSettings, Task> save, Func<ProviderAccount, bool, Task> update, Func<Task> history, Func<Task> test, Func<ProviderAccount, Task> reconnect, string? accountId = null, bool earnedResetAlerts = false)
    {
        var saveGate = new SemaphoreSlim(1, 1);
        Func<Task>? flushAccountName = null;
        var status = new Label { TextColor = Color.FromArgb("#EAAA51"), Margin = new Thickness(24, 0), HorizontalTextAlignment = TextAlignment.End };
        var selected = accounts.FirstOrDefault(a => a.Id == accountId);
        var pageTitle = selected is null ? "Settings" : $"{selected.Provider} / {selected.Nickname} \u00b7 {PlanLabels.Display(selected.Provider, snapshots.GetValueOrDefault(selected.Id)?.PlanType)}";
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
        var notifications = Toggle("Popup Notifications", original.Notifications);
        var iconAlerts = Toggle("Alert on Icon", original.AlertOnIcon);


        WrapCard(notificationStart, "Notifications", "Choose how Scrooge gets your attention.");
        var soundsStart = form.Children.Count;
        var sound = Toggle("Notification Sounds", original.Sound);
        var volumeLabel = new Label { TextColor = Color.FromArgb("#BEC1D2"), WidthRequest = 112, VerticalOptions = LayoutOptions.Center };
        var volume = new Slider { MinimumTrackColor = Color.FromArgb("#A990ED"), MaximumTrackColor = Color.FromArgb("#41434F"), ThumbColor = Color.FromArgb("#C3ADF8"), Minimum = 0, Maximum = 100, Value = double.IsFinite(original.SoundVolume) ? Math.Clamp(original.SoundVolume, 0, 100) : 65 };
        void UpdateVolumeLabel() => volumeLabel.Text = $"Volume: {Math.Round(volume.Value)}%";
        UpdateVolumeLabel();
        SemanticProperties.SetDescription(volume, "Notification Volume");
        volume.ValueChanged += (_, _) => UpdateVolumeLabel();
        var volumeRow = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 12 };
        volumeRow.Add(volumeLabel, 0); volumeRow.Add(volume, 1);
        form.Children.Add(volumeRow);
        var soundGroup = $"SoundStyle-{Guid.NewGuid():N}";
        var duckSounds = new RadioButton { Content = "Duck Sounds", GroupName = soundGroup, IsChecked = original.SoundTheme != "Classic", TextColor = Color.FromArgb("#F2F1F5") };
        var classicChimes = new RadioButton { Content = "Classic Chimes", GroupName = soundGroup, IsChecked = original.SoundTheme == "Classic", TextColor = Color.FromArgb("#F2F1F5") };
        form.Children.Add(new VerticalStackLayout { Spacing = 0, Children = { duckSounds, classicChimes } });
        var previewStatus = new Label { FontSize = 12, TextColor = Color.FromArgb("#EAAA51") };
        var previews = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)], ColumnSpacing = 8, RowSpacing = 8 };
        var previewIndex = 0;
        foreach (var cue in new[] { ("Alarm", "alert"), ("Snooze", "snooze"), ("Dismiss", "dismiss"), ("Show Info", "info") })
        {
            var preview = new ActionButton { Text = "Preview " + cue.Item1 };
            preview.Clicked += (_, _) => previewStatus.Text = AlertSounds.Play(original with { SoundTheme = classicChimes.IsChecked ? "Classic" : "Ducks", SoundVolume = volume.Value }, cue.Item2, preview: true) ?? "";
            previews.Add(preview, previewIndex % 2, previewIndex / 2); previewIndex++;
        }
        form.Children.Add(previews);
        form.Children.Add(previewStatus);
        WrapCard(soundsStart, "Sounds");
        var quietCardStart = form.Children.Count;
        var quiet = new Switch { IsToggled = original.QuietHours, OnColor = Color.FromArgb("#8170CF"), VerticalOptions = LayoutOptions.Center };
        SemanticProperties.SetDescription(quiet, "Enable Quiet Hours");
        var quietRange = new QuietHoursRange(original.QuietStartHour, original.QuietEndHour) { IsEnabled = original.QuietHours, Opacity = original.QuietHours ? 1 : 0.5 };
        quiet.Toggled += (_, e) => { quietRange.IsEnabled = e.Value; quietRange.Opacity = e.Value ? 1 : 0.5; };
        form.Children.Add(quietRange);
        WrapCard(quietCardStart, "Quiet Hours", "A little peace, right on schedule.", quiet);
        var thresholdsStart = form.Children.Count;
        var info = PercentControl("Info Alert", Math.Max(original.InfoThreshold, original.LowThreshold));
        var low = PercentControl("Warning Alert", original.LowThreshold);
        var critical = PercentControl("Critical Alert", original.CriticalThreshold);
        WrapCard(thresholdsStart, "Default Alert Levels");
        var resetStart = form.Children.Count;
        var resetReminders = new ReminderEditor(original.EffectiveResetReminders);
        form.Children.Add(resetReminders);
        var earnedResetCard = WrapCard(resetStart, "Earned Reset Expiration Alerts");
        var refreshStart = form.Children.Count;
        var refreshReminders = new ReminderEditor(original.EffectiveRefreshReminders);
        form.Children.Add(refreshReminders);
        WrapCard(refreshStart, "Quota Refresh Alerts");
        var behaviorStart = form.Children.Count;
        var close = Toggle("Keep Monitoring When Window Closes", original.CloseToTray);
        var startup = Toggle("Launch at Windows Sign-In", original.LaunchAtSignIn);
        startup.IsEnabled = !demo;
        var showHistory = new AIResetRadar.App.Controls.ActionButton { Text = "Alert History" }; showHistory.Clicked += async (_, _) => await history(); form.Children.Add(showHistory);
        var testNotification = new AIResetRadar.App.Controls.ActionButton { Text = "Send Test Notification" }; testNotification.Clicked += async (_, _) => await test(); form.Children.Add(testNotification);
        WrapCard(behaviorStart, "App Behavior");
        if (accountId is not null) form.Children.Clear();
        foreach (var account in accounts.Where(a => a.Id == accountId).ToArray())
        {
            var accountStart = form.Children.Count;
            ProviderAccount Latest() => accounts.FirstOrDefault(a => a.Id == account.Id) ?? account;
            var snapshot = snapshots.GetValueOrDefault(account.Id);
            var name = new Entry { Text = account.Nickname, Placeholder = "Personal, Work" };
            SemanticProperties.SetDescription(name, "Account Name");
            form.Children.Add(new Label { Text = "Account Name", TextColor = Color.FromArgb("#BEC1D2") });
            form.Children.Add(name);
            async Task RenameAsync()
            {
                await saveGate.WaitAsync();
                try
                {
                    var value = name.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(value) && value != Latest().Nickname)
                        await AccountAction(() => update(Latest() with { Nickname = value }, false));
                }
                finally { saveGate.Release(); }
            }
            flushAccountName = RenameAsync;
            name.Unfocused += async (_, _) => await RenameAsync();
            name.Completed += async (_, _) => await RenameAsync();
            var enabled = Toggle("Monitor This Account", account.Enabled);
            var syncing = false;
            enabled.Toggled += async (_, args) => {
                if (demo || syncing) return;
                await saveGate.WaitAsync();
                try { await update(Latest() with { Enabled = args.Value }, false); }
                catch { syncing = true; enabled.IsToggled = Latest().Enabled; syncing = false; await AccountError(); }
                finally { saveGate.Release(); }
            };
            WrapCard(accountStart, "Account");
            if (snapshot?.Reserve is { } reserve)
            {
                var start = form.Children.Count;
                form.Children.Add(new Label { Text = $"{reserve.AvailableCount?.ToString() ?? "Unknown"} Available", FontSize = 20, TextColor = Color.FromArgb("#F2F1F5") });
                foreach (var credit in reserve.Credits ?? [])
                    form.Children.Add(new Label { Text = $"Expires {TimeZones.Format(credit.ExpiresAt, TimeZones.Resolve(original.TimeZoneId), "ddd, MMM d h:mm tt")}",
                        FontSize = 14, TextColor = Color.FromArgb("#EAAA51") });
                if (reserve.HasPartialDetails) form.Children.Add(new Label { Text = "Some reset details are not supplied by the provider.", FontSize = 12, TextColor = Color.FromArgb("#B6B8C8") });
                WrapCard(start, "Available Resets");
            }
            var alertStart = form.Children.Count;
            var accountRule = original.RuleFor(account.Id);
            var exhausted = Toggle("Capacity Exhausted", accountRule.Exhausted);
            var restored = Toggle("Capacity Restored", accountRule.Restored);
            var expiry = Toggle("Reset Expiry Reminders", accountRule.Expiry);
            async Task SaveAccountAlertsAsync()
            {
                await saveGate.WaitAsync();
                try
                {
                    var rule = original.Rules?.FirstOrDefault(r => r.AccountId == account.Id && r.WindowId is null) ?? new AlertRule(account.Id);
                    rule = rule with { Exhausted = exhausted.IsToggled, Restored = restored.IsToggled, Expiry = expiry.IsToggled };
                    var candidate = original with { Rules = (original.Rules ?? []).Where(r => r.AccountId != account.Id || r.WindowId is not null).Append(rule).ToArray() };
                    await save(candidate); original = candidate; status.Text = "";
                }
                catch { status.Text = "Alerts Could Not Be Saved - Try Again"; }
                finally { saveGate.Release(); }
            }
            foreach (var toggle in new[] { exhausted, restored, expiry }) toggle.Toggled += async (_, _) => await SaveAccountAlertsAsync();
            WrapCard(alertStart, "Other Alerts", "Warning and critical levels are adjusted directly on the dashboard bars.");
            var connectionStart = form.Children.Count;
            form.Children.Add(new Label { Text = "Sign in again only if this account needs a new connection. Your saved settings stay in place.", FontSize = 13, TextColor = Color.FromArgb("#B6B8C8") });
            var signIn = new ActionButton { Text = "Sign In Again", IsEnabled = !demo };
            signIn.Clicked += async (_, _) => await AccountAction(() => reconnect(Latest()));
            form.Children.Add(signIn);
            var remove = new ActionButton { Text = "Remove Account", IsEnabled = !demo };
            remove.Clicked += async (_, _) => await AccountAction(async () => {
                if (await ThemedDialogs.ShowAsync(this, "Remove Account?", "This removes the saved connection from Scrooge. Your provider account is unaffected.", "Remove", "Cancel"))
                { await update(Latest(), true); await AppPanels.CloseAsync(this); }
            });
            form.Children.Add(remove);
            WrapCard(connectionStart, "Connection");
        }
        async Task AutoSaveAsync()
        {
            await saveGate.WaitAsync();
            try
            {
            var start = quietRange.StartHour; var end = quietRange.EndHour;
            var lowValue = (decimal)low.Value; var criticalValue = (decimal)critical.Value;
            if (quiet.IsToggled && start == end) { status.Text = "Choose different start and end times for quiet hours."; return; }
            if ((decimal)info.Value < lowValue) { status.Text = "Info Must Be At or Above Warning"; return; }
            if (criticalValue >= lowValue) { status.Text = "Critical capacity must be lower than low capacity."; return; }
            var settings = original with { ResetExpiryReminders = resetReminders.Value, QuotaRefreshReminders = refreshReminders.Value, Notifications = notifications.IsToggled, AlertOnIcon = iconAlerts.IsToggled, Sound = sound.IsToggled, Privacy = false,
                QuietHours = quiet.IsToggled, QuietStartHour = start, QuietEndHour = end, InfoThreshold = (decimal)info.Value, LowThreshold = lowValue, CriticalThreshold = criticalValue,
                LaunchAtSignIn = startup.IsToggled, CloseToTray = close.IsToggled,
                Rules = original.Rules?.Where(r => accounts.Any(a => a.Id == r.AccountId)).ToArray(),
                SoundTheme = classicChimes.IsChecked ? "Classic" : "Ducks", SoundVolume = Math.Round(volume.Value),
                TimeZoneId = (timeZone.SelectedItem as TimeZoneInfo)?.Id ?? TimeZones.DefaultId };
            if (!settings.HasValidThresholds) { status.Text = "An account/window rule conflicts with these defaults. Adjust its thresholds first."; return; }
            try { await save(settings); original = settings; status.Text = ""; }
            catch (SettingsSaveException error) { status.Text = error.Message; }
            catch { status.Text = "Settings could not be saved. Check available space and local folder access."; }
            }
            finally { saveGate.Release(); }
        }
        resetReminders.Changed += async (_, _) => await AutoSaveAsync();
        refreshReminders.Changed += async (_, _) => await AutoSaveAsync();
        volume.ValueChanged += async (_, _) => await AutoSaveAsync();
        volume.Unfocused += async (_, _) => await AutoSaveAsync();
        duckSounds.CheckedChanged += async (_, e) => { if (e.Value) await AutoSaveAsync(); };
        classicChimes.CheckedChanged += async (_, e) => { if (e.Value) await AutoSaveAsync(); };
        timeZone.SelectedIndexChanged += async (_, _) => await AutoSaveAsync();
        foreach (var toggle in new[] { notifications, iconAlerts, sound, quiet, close, startup })
            toggle.Toggled += async (_, _) => await AutoSaveAsync();
        info.ValueChanged += async (_, _) => await AutoSaveAsync();
        low.ValueChanged += async (_, _) => await AutoSaveAsync();
        critical.ValueChanged += async (_, _) => await AutoSaveAsync();
        quietRange.Changed += async (_, _) => await AutoSaveAsync();
        async Task CloseAsync()
        {
            if (flushAccountName is not null) await flushAccountName();
            await saveGate.WaitAsync();
            saveGate.Release();
            if (!string.IsNullOrEmpty(status.Text)) return;
            await AppPanels.CloseAsync(this);
        }
        closePanel = CloseAsync;
        var heading = new Grid { Padding = new Thickness(20, 12), ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        heading.Add(SectionHeading(pageTitle, selected is null ? "\uE713" : "\uE77B", 26));
        var headerClose = new AIResetRadar.App.Controls.ActionButton { Text = "Close" }; headerClose.Clicked += async (_, _) => await CloseAsync(); heading.Add(headerClose, 1);
        var layout = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto)] };
        var settingsScroll = new ScrollView { Content = form };
        layout.Add(heading); layout.Add(settingsScroll, 0, 1); layout.Add(status, 0, 2); Content = layout;
        if (earnedResetAlerts)
        {
            var positioned = false;
            earnedResetCard.SizeChanged += async (_, _) =>
            {
                if (positioned || earnedResetCard.Height <= 0 || settingsScroll.Height <= 0) return;
                positioned = true;
                await settingsScroll.ScrollToAsync(earnedResetCard, ScrollToPosition.Start, false);
            };
        }
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
        header.Add(SectionHeading(title, title switch
        {
            "Time Zone" => "\uE909",
            "Notifications" => "\uE7F4",
            "Sounds" => "\uE767",
            "Quiet Hours" => "\uE708",
            "Default Alert Levels" => "\uE9E9",
            "Earned Reset Expiration Alerts" => "\uE823",
            "Quota Refresh Alerts" => "\uE72C",
            "App Behavior" => "\uE713",
            "Account" => "\uE77B",
            "Available Resets" => "\uE8B7",
            "Other Alerts" => "\uE7F4",
            "Connection" => "\uE71B",
            _ => "\uE713"
        }));
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
    static View SectionHeading(string title, string glyph, double size = 21)
    {
        var icon = new Label { Text = glyph, FontFamily = "Segoe Fluent Icons", FontSize = size,
            TextColor = Color.FromArgb("#C3ADF8"), VerticalOptions = LayoutOptions.Center, WidthRequest = size + 3 };
        AutomationProperties.SetIsInAccessibleTree(icon, false);
        var heading = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 10 };
        heading.Add(icon);
        heading.Add(new Label { Text = title, FontSize = size, FontFamily = "Segoe UI Semibold",
            TextColor = Color.FromArgb(size == 26 ? "#F2F1F5" : "#C3ADF8"), VerticalOptions = LayoutOptions.Center }, 1);
        return heading;
    }
    Slider PercentControl(string title, decimal value)
    {
        var label = new Label { TextColor = Color.FromArgb("#BEC1D2") };
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = (double)value,
            MinimumTrackColor = Color.FromArgb("#A990ED"), MaximumTrackColor = Color.FromArgb("#41434F"), ThumbColor = Color.FromArgb("#C3ADF8") };
        SemanticProperties.SetDescription(slider, title + ": percent remaining");
        void Update() => label.Text = $"{title} @ {slider.Value:0}% Left";
        slider.ValueChanged += (_, _) => { var rounded = Math.Round(slider.Value); if (Math.Abs(slider.Value - rounded) > 0.001) slider.Value = rounded; Update(); };
        Update(); form.Children.Add(new VerticalStackLayout { Spacing = 2, Children = { label, slider } }); return slider;
    }
}
