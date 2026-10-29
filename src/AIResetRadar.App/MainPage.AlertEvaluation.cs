using AIResetRadar.App.Platforms.Windows;
using AIResetRadar.Core;
namespace AIResetRadar.App;

public sealed partial class MainPage
{
    async Task EvaluateAlertsAsync(ProviderAccount account, QuotaSnapshot? previous, QuotaSnapshot current, IReadOnlyList<AlertEvent>? recorded = null)
    {
        var now = DateTimeOffset.UtcNow;
        var lease = new AccountRefreshLease(account);
        var deliveryStatusFailed = false;
        bool Current() => lease.IsCurrent(state.Accounts.FirstOrDefault(a => a.Id == account.Id));
        foreach (var alert in recorded ?? AlertEvaluator.Evaluate(account, previous, current, preferences, now))
        {
            if (!Current()) return;
            if (!AlertEvaluator.IsEnabled(alert, preferences)) continue;
            // Persist intent before Windows delivery so a restart cannot replay the same logical event.
            if (recorded is null) { if (!await RecordAlertIntentAsync(alert)) continue; }
            else { alertHistory.RemoveAll(e => e.Key == alert.Key); alertHistory.Add(alert); }
            if (!Current()) return;
            // The user may have turned off this bar while the alert intent was saving.
            if (!AlertEvaluator.IsEnabled(alert, preferences)) continue;
            var canDeliver = preferences.Notifications && !AlertEvaluator.IsQuiet(preferences, now, TimeZones.Resolve(preferences.TimeZoneId)) && (!snoozed.TryGetValue(account.Id, out var until) || until <= now);
            var sent = canDeliver && NotificationService.Current.Show(alert, preferences, account.Provider);
            if (canDeliver) { ShowBanner(alert); AIResetRadar.App.Services.AlertSounds.Play(preferences, "alert"); }
            try { await MarkAlertDeliveredAsync(alert, sent); }
            catch { deliveryStatusFailed = true; }
        }
        alertHistory.RemoveAll(e => e.CreatedAt < HistoryRetention.Cutoff(now));
        if (deliveryStatusFailed) throw new IOException("Windows delivery status could not be saved for one or more alerts.");
    }
}
