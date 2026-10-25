namespace AIResetRadar.Core;

public sealed record AlertEvent(string Key, string AccountId, string? WindowId, string Title, string Detail,
    DateTimeOffset CreatedAt, bool Delivered = false);

public static class AlertEvaluator
{
    public static bool IsQuiet(RadarSettings settings, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (!settings.QuietHours) return false;
        var hour = TimeZoneInfo.ConvertTime(now, zone).Hour;
        return settings.QuietStartHour <= settings.QuietEndHour ? hour >= settings.QuietStartHour && hour < settings.QuietEndHour :
            hour >= settings.QuietStartHour || hour < settings.QuietEndHour;
    }
    public static IReadOnlyList<AlertEvent> Evaluate(ProviderAccount account, QuotaSnapshot? previous, QuotaSnapshot current, RadarSettings settings, DateTimeOffset now)
    {
        if (current.Health != SourceHealth.Healthy) return [];
        List<AlertEvent> alerts = [];
        foreach (var window in current.Windows)
        {
            var state = window.Capacity(settings.LowThreshold, settings.CriticalThreshold);
            var period = window.ResetsAt?.ToUnixTimeSeconds().ToString() ?? "unknown";
            if (state is CapacityState.Low or CapacityState.Critical or CapacityState.Exhausted)
            {
                var severity = state.ToString().ToLowerInvariant();
                alerts.Add(new($"{account.Id}/{window.Id}/{period}/{severity}", account.Id, window.Id,
                    $"{window.Name} {(state == CapacityState.Exhausted ? "is exhausted" : "is running low")}",
                    $"{window.RemainingPercent:0.#}% left · {account.Provider} / {account.Nickname}", now));
            }
            var before = previous?.Windows.FirstOrDefault(w => w.Id == window.Id);
            if (before?.Capacity() == CapacityState.Exhausted && window.RemainingPercent > 0 && current.UsageAllowed == true)
                alerts.Add(new($"{account.Id}/{window.Id}/{period}/restored", account.Id, window.Id, $"{window.Name} is available again",
                    $"Provider confirmed capacity · {account.Provider} / {account.Nickname}", now));
        }
        foreach (var credit in current.Reserve?.Credits ?? [])
        {
            if (credit.Status != "available" || credit.ExpiresAt is not { } expiry || expiry <= now) continue;
            var left = expiry - now;
            var threshold = left <= TimeSpan.FromHours(1) ? "1h" : left <= TimeSpan.FromHours(24) ? "24h" : null;
            if (threshold is not null) alerts.Add(new($"{account.Id}/credit/{credit.Id}/{expiry.ToUnixTimeSeconds()}/{threshold}", account.Id, null,
                "An earned Codex reset expires soon", $"Expires {expiry.ToLocalTime():MMM d, h:mm tt} · {account.Nickname}", now));
        }
        return alerts;
    }
}
