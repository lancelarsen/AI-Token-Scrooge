using AIResetRadar.Core;
using AIResetRadar.App.Services;
namespace AIResetRadar.App.Views;

public sealed class SettingsPage : ContentPage
{
    readonly VerticalStackLayout form = new() { Padding = 32, Spacing = 16, MaximumWidthRequest = 760, HorizontalOptions = LayoutOptions.Center };
    public SettingsPage(IReadOnlyList<ProviderAccount> accounts, IReadOnlyDictionary<string, QuotaSnapshot> snapshots, RadarSettings original, bool demo,
        Func<RadarSettings, Task> save, Func<ProviderAccount, bool, Task> update, Func<Task> history, Func<Task> test, Func<ProviderAccount, Task> reconnect, Action quickView)
    {
        Title = "Settings"; BackgroundColor = Color.FromArgb("#141519");
        if (demo) form.Children.Add(new Label { Text = "Demo changes stay in this preview. Notification test uses Windows.", TextColor = Color.FromArgb("#B5A0F4") });
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
        startup.IsEnabled = !demo;
        var showQuickView = new Button { Text = "Open quick view" };
        showQuickView.Clicked += (_, _) => quickView(); form.Children.Add(showQuickView);
        var showHistory = new Button { Text = "Alert history" }; showHistory.Clicked += async (_, _) => await history(); form.Children.Add(showHistory);
        var testNotification = new Button { Text = "Send test notification" }; testNotification.Clicked += async (_, _) => await test(); form.Children.Add(testNotification);
        form.Children.Add(new Label { Text = "Connected accounts", FontSize = 21, FontFamily = "Segoe UI Semibold", Margin = new Thickness(0, 16, 0, 0) });
        foreach (var account in accounts.ToArray())
        {
            var current = account;
            ProviderAccount Latest() => accounts.FirstOrDefault(a => a.Id == account.Id) ?? current;
            var plan = PlanLabels.Display(account.Provider, snapshots.GetValueOrDefault(account.Id)?.PlanType);
            var rename = new Button { Text = $"{account.Provider} / {account.Nickname} · {plan} · Rename" };
            rename.Clicked += async (_, _) => await AccountAction(async () => { var name = await DisplayPromptAsync("Account nickname", "Choose a label for this account.", initialValue: Latest().Nickname); if (!string.IsNullOrWhiteSpace(name)) { current = Latest() with { Nickname = name.Trim() }; await update(current, false); rename.Text = $"{current.Provider} / {current.Nickname} · {plan} · Rename"; } });
            form.Children.Add(rename);
            var providerPages = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 10 };
            var usagePage = new Button { Text = "Provider usage page", IsEnabled = !demo };
            var accessHelp = new Button { Text = "Usage and access help" };
            usagePage.Clicked += async (_, _) => { if (!await AIResetRadar.App.Services.ProviderNavigation.OpenAsync(Latest())) await DisplayAlertAsync("Provider page unavailable", "Open the provider's usage page in your browser.", "Close"); };
            accessHelp.Clicked += async (_, _) => { if (!await AIResetRadar.App.Services.ProviderNavigation.OpenAsync(Latest(), true)) await DisplayAlertAsync("Help unavailable", "Open your provider's usage documentation in your browser.", "Close"); };
            providerPages.Add(usagePage); providerPages.Add(accessHelp, 1); form.Children.Add(providerPages);
            var browserNotice = new Label { Text = "Browser pages use the account signed in there. Check the account/workspace before comparing.", FontSize = 12, TextColor = Color.FromArgb("#B6B8C8") }; form.Children.Add(browserNotice);
            var rules = new Button { Text = "Account and window alert rules" };
            rules.Clicked += async (_, _) => await Navigation.PushModalAsync(new AlertRulesPage(current, snapshots.GetValueOrDefault(current.Id)?.Windows ?? [], original,
                async updated => { var settings = original with { Rules = updated.Where(r => accounts.Any(a => a.Id == r.AccountId)).ToArray() }; await save(settings); original = settings; }));
            form.Children.Add(rules);
            var signIn = new Button { Text = "Reconnect account", IsEnabled = !demo }; signIn.Clicked += async (_, _) => await AccountAction(() => reconnect(Latest())); form.Children.Add(signIn);
            var enabled = Toggle("Monitor this account", account.Enabled);
            var syncing = false;
            enabled.Toggled += async (_, args) => {
                if (demo || syncing) return;
                try { current = Latest() with { Enabled = args.Value }; await update(current, false); }
                catch { syncing = true; enabled.IsToggled = Latest().Enabled; syncing = false; await AccountError(); }
            };
            var remove = new Button { Text = "Remove connection", IsEnabled = !demo };
            remove.Clicked += async (_, _) => await AccountAction(async () => { if (await DisplayAlertAsync("Remove connection?", "Radar will remove only its own saved connection for this account.", "Remove", "Cancel")) { await update(Latest(), true); form.Children.Remove(rename); form.Children.Remove(remove); form.Children.Remove(rules); form.Children.Remove(signIn); form.Children.Remove(providerPages); form.Children.Remove(browserNotice); if (enabled.Parent is View enabledRow) form.Children.Remove(enabledRow); } });
            form.Children.Add(remove);
        }
        var status = new Label { TextColor = Color.FromArgb("#EAAA51") }; form.Children.Add(status);
        var done = new Button { Text = "Save settings", BackgroundColor = Color.FromArgb("#7169CA") };
        done.Clicked += async (_, _) =>
        {
            if (!int.TryParse(quietStart.Text, out var start) || !int.TryParse(quietEnd.Text, out var end) || start is < 0 or > 23 || end is < 0 or > 23 ||
                !decimal.TryParse(low.Text, out var lowValue) || !decimal.TryParse(critical.Text, out var criticalValue) || criticalValue < 0 || lowValue > 100 || criticalValue >= lowValue)
            { status.Text = "Use valid hours and 0 ≤ critical < low ≤ 100."; return; }
            var settings = new RadarSettings(notifications.IsToggled, sound.IsToggled, privacy.IsToggled, quiet.IsToggled, start, end, lowValue, criticalValue, startup.IsToggled, close.IsToggled,
                original.Rules?.Where(r => accounts.Any(a => a.Id == r.AccountId)).ToArray());
            if (!settings.HasValidThresholds) { status.Text = "An account/window rule conflicts with these defaults. Adjust its thresholds first."; return; }
            try { await save(settings); await Navigation.PopModalAsync(); }
            catch (SettingsSaveException error) { status.Text = error.Message; }
            catch { status.Text = "Settings could not be saved. Check available space and local folder access."; }
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
        SemanticProperties.SetDescription(toggle, title);
        var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 20 };
        row.Add(new Label { Text = title, VerticalOptions = LayoutOptions.Center }); row.Add(toggle, 1); form.Children.Add(row); return toggle;
    }
    async Task AccountAction(Func<Task> action) { try { await action(); } catch { await AccountError(); } }
    Task AccountError() => DisplayAlertAsync("Account update unavailable", "Radar could not save this change. Check local folder access and retry.", "Close");
    Entry Field(string title, string value)
    {
        form.Children.Add(new Label { Text = title, TextColor = Color.FromArgb("#B6B8C8") });
        var entry = new Entry { Text = value, Keyboard = Keyboard.Numeric }; SemanticProperties.SetDescription(entry, title); form.Children.Add(entry); return entry;
    }
}
