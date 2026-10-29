using AIResetRadar.App.Controls;
using AIResetRadar.App.Platforms.Windows;
using AIResetRadar.App.Services;
using AIResetRadar.Core;
namespace AIResetRadar.App;

public sealed partial class MainPage
{
    TrayService? tray;
    Microsoft.UI.Xaml.Window? nativeWindow;
    bool exiting;
    readonly List<AlertEvent> alertHistory = [];
    readonly Dictionary<string, DateTimeOffset> snoozed = [];
    readonly Dictionary<string, DateTimeOffset> nextRefresh = [];
    IDispatcherTimer? timer;
    System.Net.NetworkInformation.NetworkAvailabilityChangedEventHandler? networkChanged;

    public void AttachWindow(Window window, Microsoft.UI.Xaml.Window native)
    {
        if (nativeWindow is not null) return;
        nativeWindow = native;
        try
        {
            tray = new(native);
            tray.Open += ShowMain;
            tray.Refresh += async () => await RefreshAsync();
            tray.ResumeOrClockChanged += async () => { nextRefresh.Clear(); if (!state.IsDemo) await RefreshAsync(); };
            tray.Exit += () => { exiting = true; Application.Current!.CloseWindow(window); };
        }
        catch { freshness.Text = "Tray is unavailable; keep the window open to monitor."; }
        NotificationService.Current.Invoked += async (action, account, quotaWindow) =>
        {
            if (action == "snooze")
            {
                if (await SaveSnoozeAsync(account)) { AlertSounds.Play(preferences, "snooze"); DismissBanner(account); }
                else
                {
                    ShowMain();
                    await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Snooze not saved", "Alerts remain active. Check available space and folder access, then try Snooze again.", "Close");
                }
            }
            else if (action == "dismiss") { AlertSounds.Play(preferences, "dismiss"); DismissBanner(account); }
            else { AlertSounds.Play(preferences, "info"); DismissBanner(account); await ShowAlertAccountAsync(account); }
        };
        NotificationService.Current.Drain();
        native.AppWindow.Closing += (_, args) =>
        {
            if (!exiting && preferences.CloseToTray && tray is not null) { args.Cancel = true; native.AppWindow.Hide(); return; }
            timer?.Stop(); if (networkChanged is not null) System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= networkChanged;
            tray?.Dispose(); NotificationService.Current.Dispose();
        };
        timer = Dispatcher.CreateTimer(); timer.Interval = TimeSpan.FromSeconds(30);
        timer.Tick += async (_, _) => { foreach (var row in rowViews) row.UpdateClock(DateTimeOffset.Now); if (!state.IsDemo) await RefreshAsync(false); else UpdateTray(); };
        timer.Start();
        networkChanged = (_, args) => { if (args.IsAvailable) Dispatcher.Dispatch(async () => { nextRefresh.Clear(); if (!state.IsDemo) await RefreshAsync(); }); };
        System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += networkChanged;
        window.Resumed += async (_, _) => { nextRefresh.Clear(); if (!state.IsDemo) await RefreshAsync(); };
        if (Environment.GetCommandLineArgs().Contains("--tray")) native.DispatcherQueue.TryEnqueue(() => native.AppWindow.Hide());
        UpdateTray();
    }
    void ShowMain()
    {
        nativeWindow?.AppWindow.Show();
        if (nativeWindow?.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter && presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Minimized) presenter.Restore();
        nativeWindow?.Activate();
    }
    void UpdateTray()
    {
        var rows = state.Rows.ToArray();
        var status = MonitorStatus.Evaluate(state.Accounts, state.Snapshots, preferences);
        var limiting = rows.FirstOrDefault(r => r.Account.Id == status.AccountId && r.Window.Id == status.WindowId);
        var unknown = status.Capacity == CapacityState.Unknown || status.HasUnavailableData || status.UsagePaused;
        var iconAlert = IconAlerts.Evaluate(state.Accounts, state.Snapshots, preferences, DateTimeOffset.UtcNow);
        var text = limiting is null ? "AI Token Scrooge" : $"Scrooge Â· {limiting.Account.Provider} / {limiting.AccountLabel} / {limiting.Window.Name}: {limiting.LeftText}";
        if (status.Notice is not null) text += " Â· " + status.Notice; if (state.IsDemo) text = "DEMO Â· " + text;
        if (iconAlert is not null) text = $"Scrooge · {iconAlert.Title} · {iconAlert.Detail}";
        tray?.Update(text, iconAlert?.Severity, preferences.AlertOnIcon && unknown);
    }
}
