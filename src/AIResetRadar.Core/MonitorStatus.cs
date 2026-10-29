namespace AIResetRadar.Core;

public sealed record MonitorStatus(CapacityState Capacity, bool HasUnavailableData, bool UsagePaused,
    string? AccountId, string? WindowId, string? Notice)
{
    public static MonitorStatus Evaluate(IEnumerable<ProviderAccount> accounts, IReadOnlyDictionary<string, QuotaSnapshot> snapshots, RadarSettings settings)
    {
        var all = accounts.ToArray(); var enabled = all.Where(a => a.Enabled).ToArray();
        if (enabled.Length == 0) return new(CapacityState.Unknown, false, false, null, null, all.Length == 0 ? "No connected accounts" : "Monitoring paused");
        bool unavailable = false, paused = false;
        List<(string Account, QuotaWindow Window, CapacityState State)> windows = [];
        foreach (var account in enabled)
        {
            if (!snapshots.TryGetValue(account.Id, out var snapshot) || !snapshot.BelongsTo(account) || snapshot.Health is not (SourceHealth.Healthy or SourceHealth.Demo))
            { unavailable = true; continue; }
            paused |= snapshot.UsageAllowed == false;
            if (snapshot.Windows.Count == 0) unavailable = true;
            foreach (var window in snapshot.Windows)
            {
                var rule = settings.RuleFor(account.Id, window.Id);
                var state = window.Capacity(rule.LowThreshold!.Value, rule.CriticalThreshold!.Value, rule.InfoThreshold!.Value);
                unavailable |= state == CapacityState.Unknown; windows.Add((account.Id, window, state));
            }
        }
        var limiting = windows.OrderByDescending(w => w.State switch { CapacityState.Exhausted => 5, CapacityState.Critical => 4, CapacityState.Low => 3, CapacityState.Info => 2, CapacityState.Healthy => 1, _ => 0 }).ThenBy(w => w.Window.RemainingPercent ?? decimal.MaxValue).FirstOrDefault();
        var notice = paused ? "Provider reports usage is paused" : unavailable ? "Some current usage data is unavailable" : null;
        return new(windows.Count == 0 ? CapacityState.Unknown : limiting.State, unavailable, paused, limiting.Account, limiting.Window?.Id, notice);
    }
}
