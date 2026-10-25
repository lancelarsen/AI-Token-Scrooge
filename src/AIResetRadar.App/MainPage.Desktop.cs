using AIResetRadar.App.Controls;
using AIResetRadar.App.Platforms.Windows;
using AIResetRadar.App.Services;
using AIResetRadar.Core;
namespace AIResetRadar.App;

public sealed partial class MainPage
{
    TrayService? tray;
    Microsoft.UI.Xaml.Window? nativeWindow;
    Window? quickWindow;
    bool exiting;
    readonly List<AlertEvent> alertHistory = [];
    readonly Dictionary<string, DateTimeOffset> snoozed = [];
    readonly Dictionary<string, DateTimeOffset> nextRefresh = [];
    IDispatcherTimer? timer;

    public void AttachWindow(Window window, Microsoft.UI.Xaml.Window native)
    {
        if (nativeWindow is not null) return;
        nativeWindow = native;
        try
        {
            tray = new(native);
            tray.Open += ShowMain; tray.QuickView += ShowQuickView;
            tray.Refresh += async () => await RefreshAsync();
            tray.ResumeOrClockChanged += async () => { nextRefresh.Clear(); if (!state.IsDemo) await RefreshAsync(); };
            tray.Exit += () => { exiting = true; if (quickWindow is not null) Application.Current!.CloseWindow(quickWindow); Application.Current!.CloseWindow(window); };
        }
        catch { freshness.Text = "Tray is unavailable; keep the window open to monitor."; }
        NotificationService.Current.Invoked += async (action, account) =>
        {
            if (action == "snooze") { snoozed[account] = DateTimeOffset.UtcNow.AddHours(1); await AppServices.Current.Store.WriteAsync("snoozed", snoozed); }
            else { ShowMain(); OpenAccount(account); }
        };
        native.AppWindow.Closing += (_, args) =>
        {
            if (!exiting && preferences.CloseToTray && tray is not null) { args.Cancel = true; native.AppWindow.Hide(); return; }
            timer?.Stop(); tray?.Dispose(); NotificationService.Current.Dispose();
        };
        timer = Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(30);
        timer.Tick += async (_, _) => { foreach (var row in rowViews) row.UpdateClock(DateTimeOffset.Now); if (!state.IsDemo) await RefreshAsync(false); else UpdateTray(); };
        timer.Start();
        window.Resumed += async (_, _) => { nextRefresh.Clear(); if (!state.IsDemo) await RefreshAsync(); };
        if (Environment.GetCommandLineArgs().Contains("--tray")) native.DispatcherQueue.TryEnqueue(() => native.AppWindow.Hide());
        UpdateTray();
    }
    void ShowMain() { nativeWindow?.AppWindow.Show(); nativeWindow?.Activate(); }
    void ShowQuickView()
    {
        if (quickWindow is not null) { if (quickWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window current) current.Activate(); return; }
        var content = new VerticalStackLayout { Padding = 20, Spacing = 16 };
        content.Children.Add(new Label { Text = "AI Reset Radar", FontSize = 23, FontFamily = "Segoe UI Semibold" });
        if (state.IsDemo) content.Children.Add(new Label { Text = "Demo data", TextColor = Color.FromArgb("#B5A0F4") });
        foreach (var row in state.Rows)
        {
            content.Children.Add(new Label { Text = $"{row.Account.Provider} / {row.Account.Nickname} · {row.Window.Name}" });
            content.Children.Add(new UsageBar(row.Window.Fill, row.UsedText, row.LeftText, row.IsWarning));
            content.Children.Add(new Label { Text = ResetTime.Countdown(row.Window.ResetsAt, DateTimeOffset.Now), TextColor = Color.FromArgb("#B5A0F4"), FontSize = 12 });
        }
        if (!state.Rows.Any()) content.Children.Add(new Label { Text = "Connect an account in the dashboard to start monitoring." });
        var open = new Button { Text = "Open dashboard", BackgroundColor = Color.FromArgb("#7169CA") };
        open.Clicked += (_, _) => { ShowMain(); if (quickWindow is not null) Application.Current!.CloseWindow(quickWindow); }; content.Children.Add(open);
        quickWindow = new Window(new ContentPage { BackgroundColor = Color.FromArgb("#141519"), Content = new ScrollView { Content = content } }) { Title = "Radar quick view", Width = 480, Height = 650, MinimumWidth = 420, MinimumHeight = 400 };
        quickWindow.Destroying += (_, _) => quickWindow = null;
        Application.Current!.OpenWindow(quickWindow);
    }
    void UpdateTray()
    {
        var rows = state.Rows.ToArray();
        var limiting = rows.OrderBy(r => r.Window.RemainingPercent ?? decimal.MaxValue).FirstOrDefault();
        var unknown = state.Accounts.Count == 0 || state.Accounts.Where(a => a.Enabled).Any(a => !state.Snapshots.TryGetValue(a.Id, out var s) || s.Health != SourceHealth.Healthy && s.Health != SourceHealth.Demo) || rows.Any(r => r.Window.RemainingPercent is null);
        var warning = rows.Any(r => r.Window.Capacity(preferences.LowThreshold, preferences.CriticalThreshold) is CapacityState.Low or CapacityState.Critical);
        var exhausted = rows.Any(r => r.Window.Capacity() == CapacityState.Exhausted);
        var text = limiting is null ? "AI Reset Radar · No usage data" : $"Radar · {limiting.Account.Nickname} / {limiting.Window.Name}: {limiting.LeftText}";
        if (unknown) text += " · Some data unavailable"; if (state.IsDemo) text = "DEMO · " + text;
        tray?.Update(text, warning, exhausted, unknown);
    }
    async Task EvaluateAlertsAsync(ProviderAccount account, QuotaSnapshot? previous, QuotaSnapshot current)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var alert in AlertEvaluator.Evaluate(account, previous, current, preferences, now))
        {
            if (alertHistory.Any(e => e.Key == alert.Key)) continue;
            var canDeliver = preferences.Notifications && !AlertEvaluator.IsQuiet(preferences, now, TimeZoneInfo.Local) && (!snoozed.TryGetValue(account.Id, out var until) || until <= now);
            var sent = canDeliver && NotificationService.Current.Show(alert, preferences);
            alertHistory.Add(alert with { Delivered = sent });
            if (canDeliver) ShowBanner(alert);
        }
        alertHistory.RemoveAll(e => e.CreatedAt < now.AddDays(-30));
        await AppServices.Current.Store.WriteAsync("alerts", alertHistory);
    }
}
