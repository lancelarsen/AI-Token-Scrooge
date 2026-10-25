using AIResetRadar.App.Services;
using AIResetRadar.Core;
namespace AIResetRadar.App;

public sealed partial class MainPage
{
    async Task<bool> RecordAlertIntentAsync(AlertEvent alert)
    {
        if (alertHistory.Any(e => e.Key == alert.Key)) return false;
        if (!state.IsDemo && !await AppServices.Current.Store.TryRecordAlertAsync(alert)) return false;
        // Failed storage never reserves a key in the in-memory suppression list.
        alertHistory.Add(alert); return true;
    }
    async Task MarkAlertDeliveredAsync(AlertEvent alert, bool delivered)
    {
        var index = alertHistory.FindIndex(e => e.Key == alert.Key);
        if (index >= 0) alertHistory[index] = alertHistory[index] with { Delivered = alertHistory[index].Delivered || delivered };
        if (!state.IsDemo) await AppServices.Current.Store.RecordAlertDeliveryAsync(alert.Key, delivered);
    }
}
