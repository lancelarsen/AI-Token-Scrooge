using AIResetRadar.App.Services;
using AIResetRadar.App.Views;
using AIResetRadar.Core;
using AIResetRadar.Infrastructure;

namespace AIResetRadar.App;

public sealed partial class MainPage
{
    async Task RefreshAsync(bool force = true, bool playSound = false)
    {
        if (!initialized || exiting) return;
        try
        {
            if (playSound)
            {
                await refresh.BeginBusyAsync("Refreshing\u2026");
                if (exiting) return;
                AlertSounds.Play(preferences, "refresh");
            }
            alertHistory.RemoveAll(e => e.CreatedAt < HistoryRetention.Cutoff(DateTimeOffset.UtcNow, preferences.HistoryRetentionDays));
            var reads = new List<Task>();
            if (!state.IsDemo)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var account in state.Accounts.Where(a => a.Enabled).ToArray())
                {
                    if (pendingReads.TryGetValue(account.Id, out var pending))
                    {
                        if (force) reads.Add(pending.Completion);
                        continue;
                    }
                    if (throttledUntil.GetValueOrDefault(account.Id) > now || !force && nextRefresh.GetValueOrDefault(account.Id) > now)
                        continue;
                    reads.Add(RefreshAccountAsync(account));
                }
            }
            if (reads.Count == 0) { Render(); UpdateTray(); }
            await Task.WhenAll(reads);
        }
        catch { freshness.Text = "Scrooge could not save local monitoring state. Check available space and folder access."; }
        finally
        {
            if (playSound) refresh.SetBusy(false);
            if (!exiting) _ = CleanupRetiredAsync();
        }
    }

    async Task RefreshAccountAsync(ProviderAccount account)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingReads.Add(account.Id, (account, timeout, ended.Task));
        var lease = new AccountRefreshLease(account);
        bool Current() => !exiting && lease.IsCurrent(state.Accounts.FirstOrDefault(a => a.Id == account.Id));
        try
        {
            QuotaSnapshot? reading = null;
            Exception? providerError = null;
            try
            {
                var monitor = AppServices.Current.Monitor;
                var result = await Task.Run(() => monitor.RefreshAsync(account, timeout.Token), timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                reading = result;
            }
            catch (Exception error) { providerError = error; }

            QuotaSnapshot? previous;
            IReadOnlyList<AlertEvent>? recorded = null;
            await accountChanges.WaitAsync();
            try
            {
                if (!Current()) return;
                previous = state.Snapshots.GetValueOrDefault(account.Id);
                var now = DateTimeOffset.UtcNow;
                QuotaSnapshot snapshot;
                if (providerError is not null)
                {
                    snapshot = RecordProviderFailure(account, previous, providerError, now);
                }
                else
                {
                    snapshot = reading!;
                    failures[account.Id] = 0;
                    nextRefresh[account.Id] = RefreshSchedule.AfterSuccess(snapshot, account, now);
                }

                // Commit this account's reading, history and alert intents together. Other
                // providers keep fetching while the shared account inventory is being saved.
                var accounts = state.Accounts.ToArray();
                var snapshots = state.Snapshots.Values.Where(s => s.AccountId != account.Id).Append(snapshot).ToArray();
                var alerts = reading is null ? [] : AlertEvaluator.Evaluate(account, previous, reading, preferences, now);
                var store = AppServices.Current.Store;
                try
                {
                    recorded = await Task.Run(() => store.WriteMonitoringStateAsync(accounts, snapshots, alerts: alerts, historySnapshot: reading));
                }
                catch
                {
                    // Preserve the last committed values so the next read can retry any alert crossing.
                    var last = previous ?? new(account.Id, account.Subject, account.Scope, now, [], null);
                    snapshot = last with { Health = SourceHealth.Stale,
                        Message = "Local history could not be saved. Check available space and folder access." };
                }
                state.Snapshots[account.Id] = snapshot;
            }
            finally { accountChanges.Release(); }

            if (!Current()) return;
            Render();
            UpdateTray();
            if (reading is not null && recorded is not null)
                await EvaluateAlertsAsync(account, previous, reading, recorded);
        }
        catch
        {
            if (Current()) freshness.Text = "Scrooge could not save local monitoring state. Check available space and folder access.";
        }
        finally
        {
            pendingReads.Remove(account.Id);
            ended.SetResult();
        }
    }

    QuotaSnapshot RecordProviderFailure(ProviderAccount account, QuotaSnapshot? previous, Exception error, DateTimeOffset now)
    {
        var count = failures.GetValueOrDefault(account.Id) + 1;
        failures[account.Id] = count;
        if (error is ProviderAccessException { RetryAt: { } retry })
            nextRefresh[account.Id] = throttledUntil[account.Id] = retry;
        else
            nextRefresh[account.Id] = now.AddSeconds(Math.Min(1800, 60 * Math.Pow(2, Math.Min(5, count))));

        var last = previous ?? new(account.Id, account.Subject, account.Scope, now, [], null);
        return last with { Health = error is ProviderAccessException access ? access.Health : SourceHealth.Offline,
            Message = ConnectAccountPage.SafeMessage(error) };
    }

    Task CancelPendingRefreshesAsync()
    {
        var pending = pendingReads.Values.ToArray();
        foreach (var read in pending) read.Cancellation.Cancel();
        return Task.WhenAll(pending.Select(read => read.Completion));
    }
}
