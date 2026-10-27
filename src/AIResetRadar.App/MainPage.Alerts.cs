using AIResetRadar.App.Platforms.Windows;
using AIResetRadar.App.Services;
using AIResetRadar.Core;
namespace AIResetRadar.App;

public sealed partial class MainPage
{
    Border? banner;
    string? bannerAccountId;
    void DismissBanner(string accountId)
    {
        if (bannerAccountId != accountId) return;
        if (banner is not null) root.Children.Remove(banner);
        banner = null; bannerAccountId = null;
    }
    void ShowBanner(AlertEvent alert)
    {
        if (banner is not null) root.Children.Remove(banner);
        var detail = new VerticalStackLayout { Spacing = 8 };
        detail.Children.Add(new Label { Text = preferences.Privacy ? "AI usage alert" : alert.Title, FontFamily = "Segoe UI Semibold", FontSize = 15 });
        detail.Children.Add(new Label { Text = preferences.Privacy ? "Open Scrooge to see details." : alert.Detail, FontSize = 12, TextColor = Color.FromArgb("#B6B8C8") });
        var actions = new HorizontalStackLayout { Spacing = 8 };
        Border? displayedBanner = null;
        var view = new AIResetRadar.App.Controls.ActionButton { Text = "View account" };
        var snooze = new AIResetRadar.App.Controls.ActionButton { Text = "Snooze 1 hour" }; var dismiss = new AIResetRadar.App.Controls.ActionButton { Text = "Dismiss" };
        view.Clicked += (_, _) => { OpenAccount(alert.AccountId, alert.WindowId); Dismiss(); };
        snooze.Clicked += async (_, _) =>
        {
            if (await SaveSnoozeAsync(alert.AccountId)) Dismiss();
            else await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Snooze not saved", "Alerts remain active. Check available space and folder access, then try Snooze again.", "Close");
        };
        dismiss.Clicked += (_, _) => Dismiss(); actions.Children.Add(view); actions.Children.Add(snooze); actions.Children.Add(dismiss); detail.Children.Add(actions);
        banner = new Border { Padding = 18, Content = detail, Stroke = Color.FromArgb("#55515F"), BackgroundColor = Color.FromArgb("#222329"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 }, MaximumWidthRequest = 520,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End, Margin = new Thickness(20) };
        root.Add(banner, 0, 2);
        bannerAccountId = alert.AccountId;
        displayedBanner = banner;
        void Dismiss()
        {
            // A completed snooze must not dismiss a newer banner shown while saving.
            if (ReferenceEquals(banner, displayedBanner)) DismissBanner(alert.AccountId);
        }
    }
    async Task OpenHistoryAsync()
    {
        alertHistory.RemoveAll(e => e.CreatedAt < HistoryRetention.Cutoff(DateTimeOffset.UtcNow));
        var page = new ContentPage { Title = "Alert history", BackgroundColor = Color.FromArgb("#141519") };
        var content = new VerticalStackLayout { Padding = 28, Spacing = 16 };
        content.Children.Add(new Label { Text = "Alert history", FontSize = 26, FontFamily = "Segoe UI Semibold" });
        if (NotificationService.Current.Problem is { } problem) content.Children.Add(new Label { Text = problem, TextColor = Color.FromArgb("#EAAA51") });
        if (alertHistory.Count == 0) content.Children.Add(new Label { Text = "No alerts yet. Low capacity and expiring resets will appear here." });
        foreach (var alert in alertHistory.OrderByDescending(a => a.CreatedAt))
        {
            var item = new AIResetRadar.App.Controls.ActionButton { Text = $"{TimeZones.Format(alert.CreatedAt, DisplayZone, "MMM d, h:mm tt")} · {alert.Title}\n{alert.Detail}\n{(alert.Delivered ? "Sent to Windows" : "Recorded in history")}", HeightRequest = -1, MinimumHeightRequest = 80 };
            item.Clicked += (_, _) => OpenAccount(alert.AccountId, alert.WindowId); content.Children.Add(item);
        }
        var back = new AIResetRadar.App.Controls.ActionButton { Text = "Close" }; back.Clicked += async (_, _) => await Navigation.PopModalAsync(); content.Children.Add(back);
        page.Content = new ScrollView { Content = content }; await Navigation.PushModalAsync(page);
    }
    async Task TestNotificationAsync()
    {
        var id = state.Accounts.FirstOrDefault()?.Id ?? "test";
        var alert = new AlertEvent("test/" + Guid.NewGuid().ToString("N"), id, null, "Scrooge notification test", "This is a test. Your account usage has not changed.", DateTimeOffset.UtcNow);
        try { if (!await RecordAlertIntentAsync(alert)) return; }
        catch { await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Local history unavailable", "Scrooge could not record the test notification. Check available space and folder access, then try again.", "Close"); return; }
        var sent = NotificationService.Current.Show(alert, preferences with { Notifications = true });
        var deliverySaved = true;
        try { await MarkAlertDeliveredAsync(alert, sent); } catch { deliverySaved = false; }
        await Navigation.PopModalAsync(); ShowBanner(alert);
        if (!sent) await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Notification test", NotificationService.Current.Problem ?? "Windows did not confirm delivery. The test is recorded in history.", "OK");
        else if (!deliverySaved) await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Notification test", "Windows accepted the notification, but Scrooge could not save the delivery status. The test will not be replayed.", "Close");
    }
}
