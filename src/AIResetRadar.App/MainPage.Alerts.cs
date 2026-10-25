using AIResetRadar.App.Platforms.Windows;
using AIResetRadar.App.Services;
using AIResetRadar.Core;
namespace AIResetRadar.App;

public sealed partial class MainPage
{
    Border? banner;
    void ShowBanner(AlertEvent alert)
    {
        if (banner is not null) root.Children.Remove(banner);
        var detail = new VerticalStackLayout { Spacing = 8 };
        detail.Children.Add(new Label { Text = preferences.Privacy ? "AI usage alert" : alert.Title, FontFamily = "Segoe UI Semibold", FontSize = 15 });
        detail.Children.Add(new Label { Text = preferences.Privacy ? "Open Radar to see details." : alert.Detail, FontSize = 12, TextColor = Color.FromArgb("#B6B8C8") });
        var actions = new HorizontalStackLayout { Spacing = 8 };
        var view = new Button { Text = "View account", BackgroundColor = Color.FromArgb("#7169CA") };
        var snooze = new Button { Text = "Snooze 1 hour" }; var dismiss = new Button { Text = "Dismiss" };
        view.Clicked += (_, _) => { OpenAccount(alert.AccountId); Dismiss(); };
        snooze.Clicked += async (_, _) => { snoozed[alert.AccountId] = DateTimeOffset.UtcNow.AddHours(1); await AppServices.Current.Store.WriteAsync("snoozed", snoozed); Dismiss(); };
        dismiss.Clicked += (_, _) => Dismiss(); actions.Children.Add(view); actions.Children.Add(snooze); actions.Children.Add(dismiss); detail.Children.Add(actions);
        banner = new Border { Padding = 18, Content = detail, Stroke = Color.FromArgb("#55515F"), BackgroundColor = Color.FromArgb("#222329"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 }, MaximumWidthRequest = 520,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End, Margin = new Thickness(20) };
        root.Add(banner, 0, 2);
        void Dismiss() { if (banner is not null) root.Children.Remove(banner); banner = null; }
    }
    async Task OpenHistoryAsync()
    {
        var page = new ContentPage { Title = "Alert history", BackgroundColor = Color.FromArgb("#141519") };
        var content = new VerticalStackLayout { Padding = 28, Spacing = 16 };
        content.Children.Add(new Label { Text = "Alert history", FontSize = 26, FontFamily = "Segoe UI Semibold" });
        if (NotificationService.Current.Problem is { } problem) content.Children.Add(new Label { Text = problem, TextColor = Color.FromArgb("#EAAA51") });
        if (alertHistory.Count == 0) content.Children.Add(new Label { Text = "No alerts yet. Low capacity and expiring resets will appear here." });
        foreach (var alert in alertHistory.OrderByDescending(a => a.CreatedAt))
        {
            var item = new Button { Text = $"{alert.CreatedAt.ToLocalTime():MMM d, h:mm tt} · {alert.Title}\n{alert.Detail}\n{(alert.Delivered ? "Sent to Windows" : "Recorded in history")}", HeightRequest = -1, MinimumHeightRequest = 80 };
            item.Clicked += (_, _) => OpenAccount(alert.AccountId); content.Children.Add(item);
        }
        var back = new Button { Text = "Close" }; back.Clicked += async (_, _) => await Navigation.PopModalAsync(); content.Children.Add(back);
        page.Content = new ScrollView { Content = content }; await Navigation.PushModalAsync(page);
    }
    async Task TestNotificationAsync()
    {
        var id = state.Accounts.FirstOrDefault()?.Id ?? "test";
        var alert = new AlertEvent("test/" + Guid.NewGuid().ToString("N"), id, null, "Radar notification test", "This is a test. Your account usage has not changed.", DateTimeOffset.UtcNow);
        var sent = NotificationService.Current.Show(alert, preferences with { Notifications = true });
        alertHistory.Add(alert with { Delivered = sent });
        await AppServices.Current.Store.WriteAsync("alerts", alertHistory);
        await Navigation.PopModalAsync(); ShowBanner(alert);
        if (!sent) await DisplayAlertAsync("Notification test", NotificationService.Current.Problem ?? "Windows did not confirm delivery. The test is recorded in history.", "OK");
    }
}
