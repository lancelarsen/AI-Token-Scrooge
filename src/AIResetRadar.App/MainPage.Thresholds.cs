using AIResetRadar.App.Services;
using AIResetRadar.Core;

namespace AIResetRadar.App;

public sealed partial class MainPage
{
    bool thresholdEditing;
    RadarSettings? thresholdStartSettings;
    string? thresholdStartSignature;

    async Task<bool> SaveThresholdsAsync(string account, string window, AlertThresholds values)
    {
        try { await SetWindowThresholdsAsync(account, window, values.Warning, values.Critical, values.Info); return true; }
        catch
        {
            await ThemedDialogs.ShowAsync(this, "Alert Levels Not Saved",
                "Scrooge could not save these alert levels. The previous levels are still active. Check local folder access and try again.", "Close");
            return false;
        }
    }

    void ThresholdInteraction(bool active)
    {
        thresholdEditing = active;
        if (active)
        {
            thresholdStartSettings = preferences;
            thresholdStartSignature = RenderSignatureFor(preferences, DateTimeOffset.Now);
            return;
        }
        // Finish the native capture before a pending data refresh can replace the control.
        Dispatcher.Dispatch(() =>
        {
            if (thresholdEditing) return;
            var now = DateTimeOffset.Now;
            var onlyLevelsChanged = thresholdStartSettings is not null && thresholdStartSignature == RenderSignatureFor(thresholdStartSettings, now);
            thresholdStartSettings = null; thresholdStartSignature = null;
            if (renderPending && (!onlyLevelsChanged || !dashboardVisible || Navigation.ModalStack.Count > 0)) { Render(); return; }
            foreach (var row in rowViews) { row.UpdateAlertRules(preferences); row.UpdateClock(now); }
            renderPending = false; UpdateFreshness(); renderSignature = RenderSignatureFor(preferences, now);
        });
    }
}
