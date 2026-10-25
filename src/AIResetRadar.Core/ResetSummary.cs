namespace AIResetRadar.Core;
public sealed record ResetSummary(long? ReportedCount, DateTimeOffset? EarliestExpiry, bool IsLastReported, bool HasExpiredDetails)
{
    public static ResetSummary Evaluate(IEnumerable<ProviderAccount> accounts, IReadOnlyDictionary<string, QuotaSnapshot> snapshots, DateTimeOffset now)
    {
        long total = 0; bool hasCount = false, partial = false, expired = false; DateTimeOffset? earliest = null;
        foreach (var account in accounts.Where(a => a.Enabled && a.Provider == "OpenAI"))
        {
            if (!snapshots.TryGetValue(account.Id, out var snapshot) || !snapshot.BelongsTo(account) || snapshot.Reserve is not { } reserve)
            { partial = true; continue; }
            if (reserve.AvailableCount is { } count) { total += count; hasCount = true; } else partial = true;
            partial |= snapshot.Health is not (SourceHealth.Healthy or SourceHealth.Demo);
            var next = reserve.EarliestExpiry(now);
            if (next is not null && (earliest is null || next < earliest)) earliest = next;
            expired |= reserve.AvailableCount != 0 && reserve.Credits?.Any(c => c.Status == "available" && c.ExpiresAt <= now) == true;
        }
        return new(hasCount ? total : null, earliest, partial || expired, expired);
    }
}
