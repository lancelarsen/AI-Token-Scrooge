using AIResetRadar.App.Services;
using AIResetRadar.Core;
namespace AIResetRadar.App;
public sealed partial class MainPage
{
    readonly SemaphoreSlim accountChanges = new(1, 1);
    readonly Dictionary<string, CancellationTokenSource> pendingReads = [];
    readonly Dictionary<string, (ProviderAccount Account, TaskCompletionSource Ended)> readCompletions = [];
    readonly List<ProviderAccount> retiredCredentials = [];
    readonly SemaphoreSlim cleanupGate = new(1, 1);
    static string CredentialKey(ProviderAccount account) => account.CredentialId ?? account.Id;
    async Task CommitConnectionAsync(ProviderAccount incoming, QuotaSnapshot snapshot, ProviderAccount? original)
    {
        ProviderAccount accepted; ProviderAccount? retired = null; QuotaSnapshot? previous;
        IReadOnlyList<AlertEvent> recorded;
        await accountChanges.WaitAsync();
        try
        {
            var index = state.Accounts.FindIndex(a => a.Id == incoming.Id);
            if (original is not null && index < 0) throw new InvalidDataException("This connection was removed. Add the account again.");
            if (original is null && state.Accounts.Any(a => a.Provider == incoming.Provider && a.Subject == incoming.Subject && a.ScopeEquals(incoming.Scope)))
                throw new InvalidDataException("This account and workspace/billing scope is already connected.");
            retired = index >= 0 ? state.Accounts[index] : null;
            accepted = retired is null ? incoming : ReconnectPolicy.Accept(retired, incoming);
            previous = state.Snapshots.GetValueOrDefault(accepted.Id);
            var accounts = state.Accounts.Where(a => a.Id != accepted.Id).Append(accepted).ToArray();
            var snapshots = state.Snapshots.Values.Where(s => s.AccountId != accepted.Id).Append(snapshot).ToArray();
            // Persist the credential reference, current snapshot, and first history sample
            // together before adopting the connection in the UI.
            var retire = retired is not null && CredentialKey(retired) != CredentialKey(accepted);
            var pending = retire ? retiredCredentials.Append(retired!).DistinctBy(CredentialKey).ToArray() : retiredCredentials.ToArray();
            var alerts = accepted.Enabled ? AlertEvaluator.Evaluate(accepted, previous, snapshot, preferences, DateTimeOffset.UtcNow) : [];
            recorded = await AppServices.Current.Store.WriteMonitoringStateAsync(accounts, snapshots, pending, alerts, historySnapshot: snapshot);
            retiredCredentials.Clear(); retiredCredentials.AddRange(pending);
            if (index >= 0) state.Accounts[index] = accepted; else state.Accounts.Add(accepted);
            state.Snapshots[accepted.Id] = snapshot;
            pendingReads.GetValueOrDefault(accepted.Id)?.Cancel(); nextRefresh.Remove(accepted.Id); failures.Remove(accepted.Id); throttledUntil.Remove(accepted.Id);
        }
        finally { accountChanges.Release(); }
        _ = CleanupRetiredAsync();
        Render(); UpdateTray();
        try { if (accepted.Enabled) await EvaluateAlertsAsync(accepted, previous, snapshot, recorded); }
        catch { freshness.Text = "Account saved. Alert history could not be updated."; }
    }
    async Task CleanupRetiredAsync()
    {
        // Demo accounts are never an authoritative inventory of saved connections.
        if (state.IsDemo || !await cleanupGate.WaitAsync(0)) return;
        try
        {
            foreach (var retired in retiredCredentials.ToArray())
            {
                try
                {
                    if (readCompletions.TryGetValue(retired.Id, out var read) && CredentialKey(read.Account) == CredentialKey(retired)) await read.Ended.Task;
                    if (state.IsDemo) return;
                    if (!state.Accounts.Any(a => CredentialKey(a) == CredentialKey(retired)))
                    { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); await AppServices.Current.Monitor.DisconnectAsync(retired, timeout.Token); }
                    if (state.IsDemo) return;
                    if (!state.Accounts.Any(a => a.Id == retired.Id)) await AppServices.Current.Store.DeleteHistoryAsync(retired.Id);
                    await accountChanges.WaitAsync();
                    try { await AppServices.Current.Store.WriteAsync("retired", retiredCredentials.Where(a => CredentialKey(a) != CredentialKey(retired)).ToArray()); retiredCredentials.RemoveAll(a => CredentialKey(a) == CredentialKey(retired)); }
                    finally { accountChanges.Release(); }
                }
                catch { freshness.Text = "Previous sign-in cleanup is pending. Scrooge will retry while monitoring."; }
            }
        }
        finally { cleanupGate.Release(); }
    }
    async Task UpdateAccountAsync(ProviderAccount edit, bool remove)
    {
        if (state.TryEditDemoAccount(edit)) { Render(); UpdateTray(); return; }
        await accountChanges.WaitAsync();
        try
        {
            var index = state.Accounts.FindIndex(a => a.Id == edit.Id); if (index < 0) return;
            var latest = state.Accounts[index];
            var changed = latest with { Nickname = edit.Nickname, Enabled = remove ? false : edit.Enabled };
            // Replacing the immutable record invalidates every read started before this edit.
            state.Accounts[index] = changed; pendingReads.GetValueOrDefault(edit.Id)?.Cancel();
            try
            {
                var accounts = state.Accounts.Where(a => !remove || a.Id != edit.Id).ToArray();
                var snapshots = state.Snapshots.Values.Where(s => !remove || s.AccountId != edit.Id).ToArray();
                var pending = remove ? retiredCredentials.Append(latest).DistinctBy(CredentialKey).ToArray() : retiredCredentials.ToArray();
                await AppServices.Current.Store.WriteMonitoringStateAsync(accounts, snapshots, pending);
                retiredCredentials.Clear(); retiredCredentials.AddRange(pending);
                if (remove) { state.Accounts.RemoveAt(index); state.Snapshots.Remove(edit.Id); }
                nextRefresh.Remove(edit.Id);
            }
            catch { state.Accounts[index] = latest with { }; throw; }
        }
        finally { accountChanges.Release(); }
        if (remove || !edit.Enabled) DismissBanner(edit.Id);
        Render(); UpdateTray();
        _ = CleanupRetiredAsync();
    }
}
