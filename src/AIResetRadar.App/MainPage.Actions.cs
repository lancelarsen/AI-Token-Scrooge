using AIResetRadar.App.Presentation;
using AIResetRadar.App.Services;
using AIResetRadar.App.Views;
using AIResetRadar.Core;
using AIResetRadar.Infrastructure;
namespace AIResetRadar.App;
public sealed partial class MainPage
{
    readonly SemaphoreSlim refreshing = new(1, 1);
    bool initialized;
    RadarSettings preferences = new();
    async Task InitializeAsync()
    {
        if (initialized) return; initialized = true;
        try
        {
            preferences = await AppServices.Current.Store.ReadAsync<RadarSettings>("settings") ?? new();
            retiredCredentials.Clear();
            alertHistory.Clear(); snoozed.Clear();
            if (!state.IsDemo)
            {
                retiredCredentials.AddRange(await AppServices.Current.Store.ReadAsync<ProviderAccount[]>("retired") ?? []);
                alertHistory.AddRange(await AppServices.Current.Store.ReadAsync<AlertEvent[]>("alerts") ?? []);
                foreach (var item in await AppServices.Current.Store.ReadAsync<Dictionary<string, DateTimeOffset>>("snoozed") ?? []) snoozed[item.Key] = item.Value;
                state.Accounts.AddRange(await AppServices.Current.Store.ReadAsync<ProviderAccount[]>("accounts") ?? []);
                foreach (var snapshot in await AppServices.Current.Store.ReadAsync<QuotaSnapshot[]>("snapshots") ?? [])
                    if (state.Accounts.Any(snapshot.BelongsTo)) state.Snapshots[snapshot.AccountId] = snapshot with { Health = SourceHealth.Stale, Message = "Saved values — refreshing" };
                Render(); await RefreshAsync();
            }
            AIResetRadar.App.Platforms.Windows.NotificationService.Current.Drain();
        }
        catch { await DisplayAlertAsync("Local storage unavailable", "Radar could not load its local account data. Check available disk space and folder access before connecting accounts.", "OK"); }
    }
    readonly Dictionary<string, int> failures = [];
    readonly Dictionary<string, DateTimeOffset> throttledUntil = [];
    async Task RefreshAsync(bool force = true)
    {
        if (!await refreshing.WaitAsync(0)) return;
        refresh.IsEnabled = false;
        try
        {
            alertHistory.RemoveAll(e => e.CreatedAt < HistoryRetention.Cutoff(DateTimeOffset.UtcNow));
            if (state.IsDemo) { Render(); return; }
            foreach (var account in state.Accounts.Where(a => a.Enabled).ToArray())
            {
                if (throttledUntil.TryGetValue(account.Id, out var throttled) && throttled > DateTimeOffset.UtcNow) continue;
                if (!force && nextRefresh.TryGetValue(account.Id, out var due) && due > DateTimeOffset.UtcNow) continue;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var lease = new AccountRefreshLease(account);
                bool Current() => lease.IsCurrent(state.Accounts.FirstOrDefault(a => a.Id == account.Id));
                if (!Current()) continue;
                pendingReads[account.Id] = timeout;
                var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); readCompletions[account.Id] = (account, ended);
                var previous = state.Snapshots.GetValueOrDefault(account.Id);
                try
                {
                    var snapshot = await AppServices.Current.Monitor.RefreshAsync(account, timeout.Token);
                    if (!Current()) continue;
                    state.Snapshots[account.Id] = snapshot; failures[account.Id] = 0;
                    await AppServices.Current.Store.RecordSnapshotAsync(snapshot);
                    if (!Current()) continue;
                    nextRefresh[account.Id] = RefreshSchedule.AfterSuccess(snapshot, account.Provider, DateTimeOffset.UtcNow);
                    await EvaluateAlertsAsync(account, previous, snapshot);
                }
                catch (Exception error)
                {
                    if (!Current()) continue;
                    var health = error is ProviderAccessException access ? access.Health : SourceHealth.Offline;
                    var last = previous ?? new(account.Id, account.Subject, account.Scope, DateTimeOffset.UtcNow, [], null);
                    state.Snapshots[account.Id] = last with { Health = health, Message = ConnectAccountPage.SafeMessage(error) };
                    var count = failures.GetValueOrDefault(account.Id) + 1; failures[account.Id] = count;
                    nextRefresh[account.Id] = error is ProviderAccessException { RetryAt: { } retry } ? retry : DateTimeOffset.UtcNow.AddSeconds(Math.Min(1800, 60 * Math.Pow(2, Math.Min(5, count))));
                    if (error is ProviderAccessException { RetryAt: { } retryAfter }) throttledUntil[account.Id] = retryAfter;
                }
                finally { pendingReads.Remove(account.Id); readCompletions.Remove(account.Id); ended.TrySetResult(); }
            }
            await SaveAsync(); Render(); UpdateTray();
        }
        catch { freshness.Text = "Radar could not save local monitoring state. Check available space and folder access."; }
        finally { refresh.IsEnabled = true; refreshing.Release(); _ = CleanupRetiredAsync(); }
    }
    async Task SaveAsync()
    {
        if (state.IsDemo) return;
        await accountChanges.WaitAsync();
        try { await AppServices.Current.Store.WriteMonitoringStateAsync(state.Accounts.ToArray(), state.Snapshots.Values.ToArray(), retiredCredentials.ToArray()); }
        finally { accountChanges.Release(); }
    }
    async Task AddAccountAsync()
    {
        var provider = await DisplayActionSheetAsync("Connect account", "Cancel", null, "OpenAI Codex", "GitHub Copilot");
        if (provider is not ("OpenAI Codex" or "GitHub Copilot")) return;
        await Navigation.PushModalAsync(new ConnectAccountPage(provider == "OpenAI Codex", (account, snapshot) => CommitConnectionAsync(account, snapshot, null)));
    }
    async Task OpenSettingsAsync() => await Navigation.PushModalAsync(new SettingsPage(state.Accounts, state.Snapshots, preferences, state.IsDemo,
        async settings => { if (!state.IsDemo) { if (settings.LaunchAtSignIn != preferences.LaunchAtSignIn) AIResetRadar.App.Platforms.Windows.StartupService.SetEnabled(settings.LaunchAtSignIn); await AppServices.Current.Store.WriteAsync("settings", settings); } preferences = settings; Render(); UpdateTray(); },
        UpdateAccountAsync, OpenHistoryAsync, TestNotificationAsync, ReconnectAsync));
    async Task ReconnectAsync(ProviderAccount original)
    {
        await Navigation.PushModalAsync(new ConnectAccountPage(original.Provider == "OpenAI", (account, snapshot) => CommitConnectionAsync(account, snapshot, original), original));
    }
    void OpenAccount(string id, string? windowId = null)
    {
        if (string.IsNullOrEmpty(windowId) && state.Accounts.Any(a => a.Id == id) && state.Snapshots.GetValueOrDefault(id)?.Reserve is not null)
        { _ = OpenReserveAsync(id); return; }
        var row = state.Rows.FirstOrDefault(r => r.Account.Id == id && (string.IsNullOrEmpty(windowId) || r.Window.Id == windowId));
        if (row is not null) _ = OpenDetailsAsync(row);
        else _ = DisplayAlertAsync("Usage details unavailable", "This account or quota window is no longer available. Refresh the dashboard to check current connections.", "Close");
    }
    async Task OpenDetailsAsync(QuotaRow row)
    {
        var details = $"{row.Window.Name}\n{row.UsedText} · {row.LeftText}\nResets: {ResetTime.Exact(row.Window.ResetsAt)}\n\nAccount: {row.Account.Login ?? row.Account.Subject}\nScope: {row.Account.Scope}\nLast successful fetch: {ResetTime.Exact(row.Snapshot.ObservedAt)}\nProvider data as of: {ResetTime.Exact(row.Snapshot.ProviderDataAt)}\n\n{row.SourceNote}";
        if (state.IsDemo || ProviderLinks.Usage(row.Account) is null) { await DisplayAlertAsync($"{row.Account.Provider} / {row.Account.Nickname}", details, "Close"); return; }
        if (await DisplayAlertAsync($"{row.Account.Provider} / {row.Account.Nickname}", details + "\n\nProvider pages use your browser's sign-in. Check its account and workspace before comparing.", "Open provider usage", "Close") && !await ProviderNavigation.OpenAsync(row.Account))
            await DisplayAlertAsync("Provider page unavailable", "Open your provider's usage page in your browser, or use Usage and access help in Settings.", "Close");
    }
    Task OpenReserveAsync(string? accountId = null)
    {
        var lines = state.Accounts.Where(a => (accountId is null ? a.Enabled : a.Id == accountId) && state.Snapshots.ContainsKey(a.Id)).Select(a => {
            var reserve = state.Snapshots[a.Id].Reserve;
            return reserve is null ? null : $"{a.Provider} / {a.Nickname}: {reserve.AvailableCount?.ToString() ?? "Unknown"} reported available\n" +
                $"Snapshot fetched {state.Snapshots[a.Id].ObservedAt.ToLocalTime():g} · {(a.Enabled ? state.Snapshots[a.Id].Health : "Monitoring paused")}\n" +
                (reserve.HasPartialDetails ? "Provider supplied only partial credit details.\n" : "") +
                string.Join("\n", reserve.Credits?.Select(c => $"• {c.ExpiresAt?.ToLocalTime().ToString("f") ?? "Expiry unavailable"} · {(c.ExpiresAt <= DateTimeOffset.UtcNow ? "Expired locally — checking provider" : c.Status)}") ?? ["Credit details unavailable"]);
        }).Where(s => s != null);
        return DisplayAlertAsync("Available resets", string.Join("\n\n", lines) is { Length: > 0 } details ? details : "Your provider has not reported earned reset availability.", "Close");
    }
}
