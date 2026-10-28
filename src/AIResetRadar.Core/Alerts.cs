namespace AIResetRadar.Core;

public sealed record AlertEvent(string Key, string AccountId, string? WindowId, string Title, string Detail,
    DateTimeOffset CreatedAt, bool Delivered = false);

public static class AlertEvaluator
{
    public static bool IsEnabled(AlertEvent alert, RadarSettings settings) => alert.WindowId is not { } windowId ||
        !(alert.Key.EndsWith("/low", StringComparison.Ordinal) || alert.Key.EndsWith("/critical", StringComparison.Ordinal) || alert.Key.EndsWith("/exhausted", StringComparison.Ordinal)) ||
        settings.CapacityAlertsEnabled(alert.AccountId, windowId);
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
            var rule = settings.RuleFor(account.Id, window.Id);
            var state = window.Capacity(rule.LowThreshold!.Value, rule.CriticalThreshold!.Value);
            var period = window.ResetsAt?.ToUnixTimeSeconds().ToString() ?? "unknown";
            var before = previous?.Windows.FirstOrDefault(w => w.Id == window.Id);
            var threshold = state == CapacityState.Exhausted ? 0 : state == CapacityState.Critical ? rule.CriticalThreshold!.Value : rule.LowThreshold!.Value;
            var crossed = before?.RemainingPercent is not { } oldLeft || before.ResetsAt != window.ResetsAt || oldLeft > threshold;
            var enabled = state switch { CapacityState.Low => rule.Low, CapacityState.Critical => rule.Critical, CapacityState.Exhausted => rule.Exhausted, _ => false };
            if (crossed && enabled && settings.CapacityAlertsEnabled(account.Id, window.Id))
            {
                var severity = state.ToString().ToLowerInvariant();
                alerts.Add(new($"{account.Id}/{window.Id}/{period}/{severity}", account.Id, window.Id,
                    $"{window.Name} {(state == CapacityState.Exhausted ? "is exhausted" : "is running low")}",
                    $"{window.RemainingPercent:0.#}% left · {account.Provider} / {account.Nickname}", now));
            }
            if (rule.Restored && before?.Capacity() == CapacityState.Exhausted && window.RemainingPercent > 0 && current.UsageAllowed == true)
                alerts.Add(new($"{account.Id}/{window.Id}/{period}/restored", account.Id, window.Id, $"{window.Name} is available again",
                    $"Provider confirmed capacity · {account.Provider} / {account.Nickname}", now));
        }
        foreach (var credit in settings.RuleFor(account.Id).Expiry && current.Reserve?.AvailableCount != 0 ? current.Reserve?.Credits ?? [] : [])
        {
            if (credit.Status != "available" || credit.ExpiresAt is not { } expiry || expiry <= now) continue;
            var left = expiry - now;
            var threshold = left <= TimeSpan.FromHours(1) ? "1h" : left <= TimeSpan.FromHours(24) ? "24h" : null;
            if (threshold is not null) alerts.Add(new($"{account.Id}/credit/{credit.Id}/{expiry.ToUnixTimeSeconds()}/{threshold}", account.Id, null,
                "An earned Codex reset expires soon", $"Expires {TimeZones.Format(expiry, TimeZones.Resolve(settings.TimeZoneId), "MMM d, h:mm tt")} · {account.Nickname}", now));
        }
        return alerts;
    }
}
