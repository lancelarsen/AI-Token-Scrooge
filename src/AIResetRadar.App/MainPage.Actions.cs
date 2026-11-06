using AIResetRadar.App.Presentation;
using AIResetRadar.App.Services;
using AIResetRadar.App.Views;
using AIResetRadar.Core;
using AIResetRadar.Infrastructure;
namespace AIResetRadar.App;
public sealed partial class MainPage
{
    bool initialized;
    bool initializing;
    RadarSettings preferences = new();
    async Task InitializeAsync()
    {
        if (initialized || initializing) return;
        initializing = true;
        AIResetRadar.App.Platforms.Windows.NotificationService.Current.AccountsLoading();
        try
        {
            var savedSettings = await AppServices.Current.Store.ReadAsync<RadarSettings>("settings");
            preferences = (savedSettings ?? new()).UpgradeReminderDefaults();
            historyPreferences = await AppServices.Current.Store.ReadAsync<HistoryPreferences>("history-view") ?? new();
            if (!state.IsDemo && preferences.LaunchAtSignIn)
            {
                try { AIResetRadar.App.Platforms.Windows.StartupService.SetEnabled(true); }
                catch { preferences = preferences with { LaunchAtSignIn = false }; }
            }
            if (!state.IsDemo && (savedSettings is null || savedSettings.ReminderDefaultsVersion < 2)) await AppServices.Current.Store.WriteAsync("settings", preferences);
            if (!state.IsDemo)
            {
                // Stage every read before adopting any account state. A failed load must
                // never let the background refresh save an empty/partial inventory.
                var saved = await AppServices.Current.Store.LoadMonitoringStateAsync();
                retiredCredentials.Clear(); retiredCredentials.AddRange(saved.Retired);
                alertHistory.Clear(); alertHistory.AddRange(saved.Alerts);
                snoozed.Clear(); foreach (var item in saved.Snoozed) snoozed[item.Key] = item.Value;
                state.Accounts.Clear(); state.Accounts.AddRange(saved.Accounts);
                state.Snapshots.Clear();
                foreach (var snapshot in saved.Snapshots)
                    if (state.Accounts.Any(snapshot.BelongsTo)) state.Snapshots[snapshot.AccountId] = snapshot with { Health = SourceHealth.Stale, Message = "Saved values — refreshing" };
                initialized = true;
                _ = AppServices.Current.Updates.CheckAsync(automatic: true, preferences.CheckForNewerVersions);
                Render();
                AIResetRadar.App.Platforms.Windows.NotificationService.Current.AccountsLoaded();
                await RefreshAsync();
            }
            else { initialized = true; AIResetRadar.App.Platforms.Windows.NotificationService.Current.AccountsLoaded(); Render(); }
        }
        catch { await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Local storage unavailable", "Scrooge could not load its local account data. Check available disk space and folder access before connecting accounts.", "OK"); }
        finally { initializing = false; }
    }
    readonly Dictionary<string, int> failures = [];
    readonly Dictionary<string, DateTimeOffset> throttledUntil = [];
    async Task RefreshByUserAsync()
    {
        if (refresh.IsBusy) return;
        if (!initialized) { await refresh.RunAsync(InitializeAsync, "Refreshing\u2026"); return; }
        await RefreshAsync(playSound: true);
    }

    async Task AddAccountAsync()
    {
        if (!initialized) { await InitializeAsync(); if (!initialized) return; }
        var provider = await AIResetRadar.App.Services.ThemedDialogs.ChooseAsync(this, "Connect Account", "OpenAI Codex", "GitHub Copilot", "Claude", "Devin");
        if (provider is "Claude" or "Devin") { await AppPanels.ShowAsync(this, new ConnectBrowserAccountPage(provider, (account, snapshot) => CommitConnectionAsync(account, snapshot, null))); return; }
        if (provider is not ("OpenAI Codex" or "GitHub Copilot")) return;
        await AppPanels.ShowAsync(this, new ConnectAccountPage(provider == "OpenAI Codex", (account, snapshot) => CommitConnectionAsync(account, snapshot, null)));
    }
    async Task OpenSettingsAsync(bool earnedResetAlerts = false)
    {
        if (!initialized) { await InitializeAsync(); if (!initialized) return; }
        await AppPanels.ShowAsync(this, new SettingsPage(state.Accounts, state.Snapshots, preferences, state.IsDemo,
        SaveSettingsAsync,
        UpdateAccountAsync, OpenHistoryAsync, TestNotificationAsync, ReconnectAsync, earnedResetAlerts: earnedResetAlerts, restartUpdate: RestartForUpdateAsync));
    }
    async Task OpenAccountSettingsAsync(string accountId)
    {
        if (!state.Accounts.Any(a => a.Id == accountId)) return;
        await AppPanels.ShowAsync(this, new SettingsPage(state.Accounts, state.Snapshots, preferences, state.IsDemo,
            SaveSettingsAsync, UpdateAccountAsync, OpenHistoryAsync, TestNotificationAsync, ReconnectAsync, accountId));
    }
    async Task ReconnectAsync(ProviderAccount original)
    {
        if (original.Provider is "Claude" or "Devin") { await AppPanels.ShowAsync(this, new ConnectBrowserAccountPage(original.Provider, (account, snapshot) => CommitConnectionAsync(account, snapshot, original), original)); return; }
        await AppPanels.ShowAsync(this, new ConnectAccountPage(original.Provider == "OpenAI", (account, snapshot) => CommitConnectionAsync(account, snapshot, original), original));
    }
    void OpenAccount(string id, string? windowId = null)
    {
        if (string.IsNullOrEmpty(windowId) && state.Accounts.Any(a => a.Id == id) && state.Snapshots.GetValueOrDefault(id)?.Reserve is not null)
        { _ = OpenReserveAsync(id); return; }
        var row = state.Rows.FirstOrDefault(r => r.Account.Id == id && (string.IsNullOrEmpty(windowId) || r.Window.Id == windowId));
        if (row is not null) _ = OpenDetailsAsync(row);
        else _ = AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Usage details unavailable", "This account or quota window is no longer available. Refresh the dashboard to check current connections.", "Close");
    }
    async Task OpenDetailsAsync(QuotaRow row)
    {
        var details = $"{row.WindowTitle}\n{row.UsedText} · {row.LeftText}\nResets: {ResetTime.Exact(row.Window.ResetsAt, DisplayZone)}\n\nAccount: {row.Account.Login ?? row.Account.Subject}\nScope: {row.Account.Scope}\nLast successful fetch: {ResetTime.Exact(row.Snapshot.ObservedAt, DisplayZone)}\nProvider data as of: {ResetTime.Exact(row.Snapshot.ProviderDataAt, DisplayZone)}\n\n{row.SourceNote}";
        if (state.IsDemo || ProviderLinks.Usage(row.Account) is null) { await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, $"{row.Account.Provider} / {row.AccountLabel}", details, "Close"); return; }
        if (await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, $"{row.Account.Provider} / {row.AccountLabel}", details + "\n\nProvider pages use your browser's sign-in. Check its account and workspace before comparing.", "Open provider usage", "Close") && !await ProviderNavigation.OpenAsync(row.Account))
            await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Provider page unavailable", "Open your provider's usage page in your browser, and check its connection status.", "Close");
    }
    Task OpenReserveAsync(string? accountId = null)
    {
        var lines = state.Accounts.Where(a => (accountId is null ? a.Enabled : a.Id == accountId) && state.Snapshots.ContainsKey(a.Id)).Select(a => {
            var reserve = state.Snapshots[a.Id].Reserve;
            return reserve is null ? null : $"{a.Provider} / {a.Nickname} · {PlanLabels.Display(a.Provider, state.Snapshots[a.Id].PlanType)}: {reserve.AvailableCount?.ToString() ?? "Unknown"} reported available\n" +
                $"Snapshot fetched {TimeZones.Format(state.Snapshots[a.Id].ObservedAt, DisplayZone, "g")} · {(a.Enabled ? state.Snapshots[a.Id].Health : "Monitoring paused")}\n" +
                (reserve.HasPartialDetails ? "Provider supplied only partial credit details.\n" : "") +
                string.Join("\n", reserve.Credits?.Select(c => $"• {TimeZones.Format(c.ExpiresAt, DisplayZone)} · {(c.ExpiresAt <= DateTimeOffset.UtcNow ? "Expired locally — checking provider" : c.Status)}") ?? ["Credit details unavailable"]);
        }).Where(s => s != null);
        return AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Available resets", string.Join("\n\n", lines) is { Length: > 0 } details ? details : "Your provider has not reported earned reset availability.", "Close");
    }
}
