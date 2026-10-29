namespace AIResetRadar.Core;

public static class IconAlerts
{
    // A persistent status indicator is independent of popup delivery and quiet hours.
    public static AlertEvent? Evaluate(IEnumerable<ProviderAccount> accounts,
        IReadOnlyDictionary<string, QuotaSnapshot> snapshots, RadarSettings settings, DateTimeOffset now)
    {
        if (!settings.AlertOnIcon) return null;
        return accounts.Where(a => a.Enabled && snapshots.TryGetValue(a.Id, out var s) && s.BelongsTo(a))
            .SelectMany(a => AlertEvaluator.Evaluate(a, null, snapshots[a.Id], settings, now))
            .Where(a => a.Severity.HasValue && AlertEvaluator.IsEnabled(a, settings))
            .OrderByDescending(a => a.Severity).FirstOrDefault();
    }
}
