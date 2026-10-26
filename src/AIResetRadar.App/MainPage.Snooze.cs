using AIResetRadar.App.Services;
namespace AIResetRadar.App;

public sealed partial class MainPage
{
    readonly SemaphoreSlim snoozeGate = new(1, 1);

    async Task<bool> SaveSnoozeAsync(string account)
    {
        await snoozeGate.WaitAsync();
        try
        {
            var until = DateTimeOffset.UtcNow.AddHours(1);
            var updated = new Dictionary<string, DateTimeOffset>(snoozed) { [account] = until };
            if (!state.IsDemo)
            {
                try { await AppServices.Current.Store.WriteAsync("snoozed", updated); }
                catch { return false; }
            }
            snoozed[account] = until;
            return true;
        }
        finally { snoozeGate.Release(); }
    }
}
