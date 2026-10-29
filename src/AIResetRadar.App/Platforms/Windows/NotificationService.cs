using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using AIResetRadar.Core;
namespace AIResetRadar.App.Platforms.Windows;

public sealed class NotificationService : IDisposable
{
    public static NotificationService Current { get; } = new();
    readonly NotificationActivationInbox pending = new();
    bool accountsLoaded;
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
        pending.Enqueue(new(action, account, window));
        if (Invoked is not null) MainThread.BeginInvokeOnMainThread(Drain);
    }
    public void AccountsLoading() => accountsLoaded = false;
    public void AccountsLoaded() { accountsLoaded = true; Drain(); }
    public void Drain()
    {
        var handler = Invoked;
        pending.Drain(accountsLoaded, handler is null ? null : item => handler(item.Action, item.AccountId, item.WindowId));
    }
    public bool Show(AlertEvent alert, RadarSettings settings, string provider = "Scrooge")
    {
        if (!registered || !settings.Notifications) return false;
        try
        {
            if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled)
            { Problem = "Windows has disabled popup notifications for Scrooge. Events remain available in Alert history."; return false; }
            Problem = null;
            var builder = new AppNotificationBuilder().AddArgument("action", "view").AddArgument("account", alert.AccountId).AddArgument("window", alert.WindowId ?? "")
                .AddText(alert.Title).AddText(alert.Detail)
                .AddButton(new AppNotificationButton("Show Info").AddArgument("action", "view").AddArgument("account", alert.AccountId).AddArgument("window", alert.WindowId ?? ""))
                .AddButton(new AppNotificationButton("Snooze 1 Hour").AddArgument("action", "snooze").AddArgument("account", alert.AccountId));
            builder.AddButton(new AppNotificationButton("Dismiss").AddArgument("action", "dismiss").AddArgument("account", alert.AccountId));
            var image = provider switch { "OpenAI" => "openai", "Claude" => "claude", "GitHub Copilot" => "github", _ => "token_baron" };
            var imagePath = Path.Combine(AppContext.BaseDirectory, image + ".scale-100.png");
            if (File.Exists(imagePath)) builder.SetAppLogoOverride(new Uri(imagePath));
            // Scrooge plays its selected cue once for both the Windows toast and in-app banner.
            builder.MuteAudio();
            var notification = builder.BuildNotification(); notification.Expiration = DateTimeOffset.Now.AddHours(1);
            AppNotificationManager.Default.Show(notification);
            if (notification.Id == 0) Problem = "Windows did not confirm notification acceptance. Check Windows notification settings.";
            return notification.Id != 0;
        }
        catch { Problem = "Windows could not deliver this notification. Check Windows notification settings for Scrooge."; return false; }
    }
    public void Dispose() { try { AppNotificationManager.Default.NotificationInvoked -= OnInvoked; AppNotificationManager.Default.Unregister(); } catch { } }
}
