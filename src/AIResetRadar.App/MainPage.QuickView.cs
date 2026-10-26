using AIResetRadar.App.Controls;
using AIResetRadar.Core;
namespace AIResetRadar.App;

public sealed partial class MainPage
{
    VerticalStackLayout? quickContent;
    string? quickSignature;

    void ShowQuickView()
    {
        if (quickWindow is not null)
        {
            UpdateQuickView();
            if (quickWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window current) current.Activate();
            return;
        }
        quickContent = new VerticalStackLayout { Spacing = 16 };
        var content = new VerticalStackLayout { Padding = 20, Spacing = 16 };
        content.Children.Add(new Label { Text = "AI Token Scrooge", FontSize = 23, FontFamily = "Segoe UI Semibold" });
        content.Children.Add(new Label { Text = "Never Waste a Token!", FontSize = 12, TextColor = Color.FromArgb("#C3ADF8") });
        content.Children.Add(quickContent);
        var open = new Button { Text = "Open dashboard", BackgroundColor = Color.FromArgb("#7169CA") };
        open.Clicked += (_, _) => { ShowMain(); if (quickWindow is not null) Application.Current!.CloseWindow(quickWindow); };
        content.Children.Add(open);
        quickSignature = null;
        UpdateQuickView();
        quickWindow = new Window(new ContentPage
        {
            BackgroundColor = Color.FromArgb("#141519"), Content = new ScrollView { Content = content }
        }) { Title = "Scrooge quick view", Width = 480, Height = 650, MinimumWidth = 420, MinimumHeight = 400 };
        quickWindow.Destroying += (_, _) => { quickWindow = null; quickContent = null; quickSignature = null; };
        Application.Current!.OpenWindow(quickWindow);
    }

    void UpdateQuickView()
    {
        if (quickContent is null) return;
        var now = DateTimeOffset.Now;
        var rows = state.Rows.Select(row => new
        {
            Title = $"{row.Account.Provider} / {row.AccountLabel} · {row.Window.Name}",
            row.Window.Fill, row.UsedText, row.LeftText, row.IsWarning, row.SourceNote,
            Reset = ResetTime.Countdown(row.Window.ResetsAt, now)
        }).ToArray();
        var events = state.Events(now).Select(item => $"{item.Title} · {item.Detail}\n{item.At.ToLocalTime():MMM d, h:mm tt zzz}").ToArray();
        var signature = System.Text.Json.JsonSerializer.Serialize(new { state.IsDemo, AccountCount = state.Accounts.Count, rows, events });
        if (signature == quickSignature) return;
        quickSignature = signature;
        quickContent.Children.Clear();
        if (state.IsDemo) quickContent.Children.Add(new Label { Text = "Demo data", TextColor = Color.FromArgb("#B5A0F4") });
        foreach (var row in rows)
        {
            quickContent.Children.Add(new Label { Text = row.Title });
            quickContent.Children.Add(new UsageBar(row.Fill, row.UsedText, row.LeftText, row.IsWarning));
            if (!string.IsNullOrEmpty(row.SourceNote)) quickContent.Children.Add(new Label { Text = row.SourceNote, FontSize = 12, TextColor = Color.FromArgb("#B6B8C8") });
            quickContent.Children.Add(new Label { Text = row.Reset, TextColor = Color.FromArgb("#B5A0F4"), FontSize = 12 });
        }
        if (rows.Length == 0) quickContent.Children.Add(new Label { Text = state.Accounts.Count == 0 ? "Connect an account in the dashboard to start monitoring." : "No quota windows available. Open the dashboard to check account status." });
        if (events.Length > 0)
        {
            quickContent.Children.Add(new Label { Text = "Upcoming resets and expirations", FontFamily = "Segoe UI Semibold" });
            foreach (var item in events) quickContent.Children.Add(new Label { Text = item, FontSize = 12, TextColor = Color.FromArgb("#B6B8C8") });
        }
    }
}
