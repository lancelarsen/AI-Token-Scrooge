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
            alertHistory.AddRange(await AppServices.Current.Store.ReadAsync<AlertEvent[]>("alerts") ?? []);
            foreach (var item in await AppServices.Current.Store.ReadAsync<Dictionary<string, DateTimeOffset>>("snoozed") ?? []) snoozed[item.Key] = item.Value;
            if (!state.IsDemo)
            {
                state.Accounts.AddRange(await AppServices.Current.Store.ReadAsync<ProviderAccount[]>("accounts") ?? []);
                foreach (var snapshot in await AppServices.Current.Store.ReadAsync<QuotaSnapshot[]>("snapshots") ?? [])
                    if (state.Accounts.Any(a => a.Id == snapshot.AccountId)) state.Snapshots[snapshot.AccountId] = snapshot with { Health = SourceHealth.Stale, Message = "Saved values — refreshing" };
                Render(); await RefreshAsync();
            }
            AIResetRadar.App.Platforms.Windows.NotificationService.Current.Drain();
        }
        catch { await DisplayAlertAsync("Local storage unavailable", "Radar could not load its local account data. Check available disk space and folder access before connecting accounts.", "OK"); }
    }
    readonly Dictionary<string, int> failures = [];
    async Task RefreshAsync(bool force = true)
    {
        if (!await refreshing.WaitAsync(0)) return;
        refresh.IsEnabled = false;
        try
        {
            if (state.IsDemo) { Render(); return; }
            foreach (var account in state.Accounts.Where(a => a.Enabled).ToArray())
            {
                if (!force && nextRefresh.TryGetValue(account.Id, out var due) && due > DateTimeOffset.UtcNow) continue;
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var previous = state.Snapshots.GetValueOrDefault(account.Id);
                try
                {
                    var snapshot = await AppServices.Current.Monitor.RefreshAsync(account, timeout.Token);
                    state.Snapshots[account.Id] = snapshot; failures[account.Id] = 0;
                    nextRefresh[account.Id] = DateTimeOffset.UtcNow.AddSeconds(account.Provider == "OpenAI" ? 120 : 300);
                    await EvaluateAlertsAsync(account, previous, snapshot);
                }
                catch (Exception error)
                {
                    var health = error is ProviderAccessException access ? access.Health : SourceHealth.Offline;
                    var last = previous ?? new(account.Id, account.Subject, account.Scope, DateTimeOffset.UtcNow, [], null);
                    state.Snapshots[account.Id] = last with { Health = health, Message = ConnectAccountPage.SafeMessage(error) };
                    var count = failures.GetValueOrDefault(account.Id) + 1; failures[account.Id] = count;
                    nextRefresh[account.Id] = error is ProviderAccessException { RetryAt: { } retry } ? retry : DateTimeOffset.UtcNow.AddSeconds(Math.Min(1800, 60 * Math.Pow(2, Math.Min(5, count))));
                }
            }
            await SaveAsync(); Render(); UpdateTray();
        }
        finally { refresh.IsEnabled = true; refreshing.Release(); }
    }
    async Task SaveAsync()
    {
        if (state.IsDemo) return;
        await AppServices.Current.Store.WriteAsync("accounts", state.Accounts);
        await AppServices.Current.Store.WriteAsync("snapshots", state.Snapshots.Values.ToArray());
    }
    async Task AddAccountAsync()
    {
        var provider = await DisplayActionSheetAsync("Connect account", "Cancel", null, "OpenAI Codex", "GitHub Copilot");
        if (provider is not ("OpenAI Codex" or "GitHub Copilot")) return;
        await Navigation.PushModalAsync(new ConnectAccountPage(provider == "OpenAI Codex", async (account, snapshot) =>
        {
            if (state.Accounts.Any(a => a.Provider == account.Provider && a.Subject == account.Subject && a.Scope == account.Scope))
            {
                await AppServices.Current.Monitor.DisconnectAsync(account);
                throw new InvalidDataException("This account and billing/workspace scope is already connected.");
            }
            state.Accounts.Add(account); state.Snapshots[account.Id] = snapshot;
            await SaveAsync(); Render();
        }));
    }
    async Task OpenSettingsAsync() => await Navigation.PushModalAsync(new SettingsPage(state.Accounts, preferences, state.IsDemo,
        async settings => { if (settings.LaunchAtSignIn != preferences.LaunchAtSignIn) AIResetRadar.App.Platforms.Windows.StartupService.SetEnabled(settings.LaunchAtSignIn); preferences = settings; await AppServices.Current.Store.WriteAsync("settings", settings); Render(); UpdateTray(); },
        async (account, remove) => {
            if (remove) { await AppServices.Current.Monitor.DisconnectAsync(account); state.Accounts.RemoveAll(a => a.Id == account.Id); state.Snapshots.Remove(account.Id); }
            else { var index = state.Accounts.FindIndex(a => a.Id == account.Id); if (index >= 0) state.Accounts[index] = account; }
            await SaveAsync(); Render();
        }, OpenHistoryAsync, TestNotificationAsync));
    void OpenAccount(string id) { var row = state.Rows.FirstOrDefault(r => r.Account.Id == id); if (row is not null) _ = OpenDetailsAsync(row); }
    Task OpenDetailsAsync(QuotaRow row) => DisplayAlertAsync($"{row.Account.Provider} / {row.Account.Nickname}", $"{row.Window.Name}\n{row.UsedText} · {row.LeftText}\nResets: {row.Window.ResetsAt?.ToLocalTime():f}\n\n{row.Snapshot.Message}\n{row.SourceNote}", "Close");
    Task OpenReserveAsync()
    {
        var lines = state.Accounts.Where(a => state.Snapshots.ContainsKey(a.Id)).Select(a => {
            var reserve = state.Snapshots[a.Id].Reserve;
            return reserve is null ? null : $"{a.Provider} / {a.Nickname}: {reserve.AvailableCount?.ToString() ?? "Unknown"} reported available\n" + string.Join("\n", reserve.Credits?.Select(c => $"• {c.ExpiresAt?.ToLocalTime().ToString("f") ?? "Expiry unavailable"} · {c.Status}") ?? ["Credit details unavailable"]);
        }).Where(s => s != null);
        return DisplayAlertAsync("Available resets", string.Join("\n\n", lines) is { Length: > 0 } details ? details : "Your provider has not reported earned reset availability.", "Close");
    }
}
