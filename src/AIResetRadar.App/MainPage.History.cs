using AIResetRadar.App.Controls;
using AIResetRadar.App.Presentation;
using AIResetRadar.App.Services;
using AIResetRadar.Core;

namespace AIResetRadar.App;

public sealed partial class MainPage
{
    HorizonView? horizon;
    HistoryPreferences historyPreferences = new();
    HistorySeries[] usageHistory = [];
    Dictionary<string, UsageHistoryCache> historyCache = [];
    CancellationTokenSource? historyRead;
    readonly SemaphoreSlim historySave = new(1, 1);
    string? historyReadSignature;
    string? historyError;
    bool historyLoading;

    HistorySeries[] OrderedUsageHistory()
    {
        var byId = usageHistory.ToDictionary(s => s.Account.Id);
        return AccountOrdering.Normalize(preferences.AccountOrder, state.Accounts.Select(a => a.Id))
            .Where(byId.ContainsKey).Select(id => byId[id]).ToArray();
    }

    void UpdateHorizon()
    {
        var now = DateTimeOffset.Now;
        var accounts = state.Accounts.Where(a => a.Enabled && UsageHistory.SupportsProvider(a.Provider)).OrderBy(a => a.Id, StringComparer.Ordinal).ToArray();
        var signature = System.Text.Json.JsonSerializer.Serialize(new {
            Accounts = accounts, Zone = DisplayZone.Id, state.IsDemo,
            Date = TimeZoneInfo.ConvertTime(now, DisplayZone).Date,
            Samples = accounts.Select(a => state.Snapshots.GetValueOrDefault(a.Id)?.ObservedAt)
        });
        if (historyReadSignature != signature)
        {
            historyReadSignature = signature;
            historyRead?.Cancel(); historyRead?.Dispose(); historyRead = new();
            // Remove disconnected identities immediately, even while the replacement query is running.
            usageHistory = accounts.Select(account => usageHistory.FirstOrDefault(s => s.Account == account)
                ?? new HistorySeries(account, account.Provider == "GitHub Copilot" ? "Monthly" : "Weekly",
                    PlanLabels.Display(account.Provider, state.Snapshots.GetValueOrDefault(account.Id)?.PlanType), [], [])).ToArray();
            historyLoading = true; historyError = null;
            _ = LoadUsageHistoryAsync(accounts, now, DisplayZone, state.IsDemo,
                state.Snapshots.ToDictionary(p => p.Key, p => p.Value), signature, historyRead.Token);
        }
        if (horizon is null)
        {
            horizon = new HorizonView(state.Events(now), e => OpenAccount(e.AccountId, e.WindowId), DisplayZone,
                async () => await OpenSettingsAsync(true), enabled => SaveHistoryChoiceAsync(p => p with { Enabled = enabled }),
                (id, visible) => SaveHistoryChoiceAsync(p => p.WithAccount(id, visible)));
            body.Children.Insert(0, horizon);
        }
        horizon.Update(state.Events(now), OrderedUsageHistory(), historyPreferences, DisplayZone, now, historyLoading, historyError);
    }

    async Task LoadUsageHistoryAsync(ProviderAccount[] accounts, DateTimeOffset now, TimeZoneInfo zone, bool demo,
        Dictionary<string, QuotaSnapshot> snapshots, string signature, CancellationToken ct)
    {
        try
        {
            var cached = historyCache;
            var updatedCache = new Dictionary<string, UsageHistoryCache>();
            var data = await Task.Run(async () => {
                if (demo) return HistoryDemo.Build(accounts, snapshots, now, zone);
                var range = new HorizonViewport(now, null, zone, 700);
                var result = new List<HistorySeries>();
                foreach (var account in accounts)
                {
                    ct.ThrowIfCancellationRequested();
                    var prior = cached.GetValueOrDefault(account.Id);
                    if (prior is not null && !prior.Covers(account, range.StartAt)) prior = null;
                    // The database revision also notices an extended unchanged interval.
                    // All cached state is replaced only after this load wins the signature check.
                    var page = await AppServices.Current.Store.HistoryPageAsync(account.Id, range.StartAt, now,
                        afterRevision: prior?.Revision, ct: ct);
                    var next = (prior ?? new UsageHistoryCache(account, range.StartAt, 0, [])).Append(account, range.StartAt, page);
                    updatedCache.Add(account.Id, next);
                    result.Add(next.Build(now));
                }
                return result.ToArray();
            }, ct);
            if (ct.IsCancellationRequested || historyReadSignature != signature) return;
            historyCache = updatedCache;
            usageHistory = data; historyLoading = false; historyError = null;
            horizon?.Update(state.Events(DateTimeOffset.Now), OrderedUsageHistory(), historyPreferences, DisplayZone, DateTimeOffset.Now);
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (ct.IsCancellationRequested || historyReadSignature != signature) return;
            historyLoading = false; historyError = "History Could Not Be Loaded · Refresh to Retry";
            historyReadSignature = null;
            horizon?.Update(state.Events(DateTimeOffset.Now), OrderedUsageHistory(), historyPreferences, DisplayZone, DateTimeOffset.Now, error: historyError);
        }
    }

    async Task<bool> SaveHistoryChoiceAsync(Func<HistoryPreferences, HistoryPreferences> change)
    {
        await historySave.WaitAsync();
        try
        {
            var next = change(historyPreferences).ForAccounts(state.Accounts.Select(a => a.Id));
            if (!state.IsDemo) await AppServices.Current.Store.WriteAsync("history-view", next);
            historyPreferences = next;
            horizon?.Update(state.Events(DateTimeOffset.Now), OrderedUsageHistory(), historyPreferences, DisplayZone, DateTimeOffset.Now, historyLoading, historyError);
            return true;
        }
        catch
        {
            await ThemedDialogs.ShowAsync(this, "History Choice Not Saved", "Scrooge could not save this choice. Check local folder access and try again.", "Close");
            return false;
        }
        finally { historySave.Release(); }
    }
}
