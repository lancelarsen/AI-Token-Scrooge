using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using AIResetRadar.Core;
namespace AIResetRadar.App.Platforms.Windows;

public sealed class NotificationService : IDisposable
{
    public static NotificationService Current { get; } = new();
    readonly System.Collections.Concurrent.ConcurrentQueue<(string Action, string Account, string? Window)> pending = new();
    bool registered;
    public string? Problem { get; private set; }
    public event Action<string, string, string?>? Invoked;
    public NotificationService()
    {
        try { AppNotificationManager.Default.NotificationInvoked += OnInvoked; AppNotificationManager.Default.Register(); registered = true; }
        catch { Problem = "Windows popup notifications are unavailable in this app session. Alert history remains available."; }
    }
    void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var action = args.Arguments.ContainsKey("action") ? args.Arguments["action"] : "view";
        var account = args.Arguments.ContainsKey("account") ? args.Arguments["account"] : "";
        var window = args.Arguments.ContainsKey("window") ? args.Arguments["window"] : null;
        pending.Enqueue((action, account, window));
        if (Invoked is not null) MainThread.BeginInvokeOnMainThread(Drain);
    }
    public void Drain() { while (pending.TryDequeue(out var item)) Invoked?.Invoke(item.Action, item.Account, item.Window); }
    public bool Show(AlertEvent alert, RadarSettings settings)
    {
        if (!registered || !settings.Notifications) return false;
        try
        {
            if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled)
            { Problem = "Windows has disabled popup notifications for Radar. Events remain available in Alert history."; return false; }
            Problem = null;
            var builder = new AppNotificationBuilder().AddArgument("action", "view").AddArgument("account", alert.AccountId).AddArgument("window", alert.WindowId ?? "")
                .AddText(settings.Privacy ? "AI usage alert" : alert.Title).AddText(settings.Privacy ? "Open Radar to see account details." : alert.Detail)
                .AddButton(new AppNotificationButton("View account").AddArgument("action", "view").AddArgument("account", alert.AccountId).AddArgument("window", alert.WindowId ?? ""))
                .AddButton(new AppNotificationButton("Snooze 1 hour").AddArgument("action", "snooze").AddArgument("account", alert.AccountId));
            if (!settings.Sound) builder.MuteAudio();
            var notification = builder.BuildNotification(); notification.Expiration = DateTimeOffset.Now.AddHours(1);
            AppNotificationManager.Default.Show(notification);
            if (notification.Id == 0) Problem = "Windows did not confirm notification acceptance. Check Windows notification settings.";
            return notification.Id != 0;
        }
        catch { Problem = "Windows could not deliver this notification. Check Windows notification settings for Radar."; return false; }
    }
    public void Dispose() { try { AppNotificationManager.Default.NotificationInvoked -= OnInvoked; AppNotificationManager.Default.Unregister(); } catch { } }
}
