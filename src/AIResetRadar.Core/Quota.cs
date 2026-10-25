namespace AIResetRadar.Core;

public enum UsageUnit { Percent, Credits, Requests }
public enum SourceHealth { Healthy, Loading, Stale, Offline, AuthenticationRequired, PermissionRequired, Unsupported, Demo }
public enum AllowanceProvenance { Unknown, Provider, Manual }
public enum CapacityState { Unknown, Healthy, Low, Critical, Exhausted }

public sealed record ProviderAccount(string Id, string Provider, string Nickname, string Subject, string Scope,
    bool Enabled = true, decimal? Allowance = null, UsageUnit Unit = UsageUnit.Credits, string? Login = null, string? CredentialId = null);

public sealed record QuotaWindow(string Id, string Name, UsageUnit Unit, decimal? Used,
    decimal? Limit, DateTimeOffset? ResetsAt, int? DurationMinutes = null,
    AllowanceProvenance AllowanceSource = AllowanceProvenance.Unknown)
{
    public decimal? UsedPercent => Used is not (>= 0) ? null : Unit == UsageUnit.Percent ? Used :
        Limit is > 0 ? Used / Limit * 100 : null;
    public decimal? Remaining => Used is not (>= 0) ? null : Unit == UsageUnit.Percent ?
        Math.Max(0, 100 - Used.Value) : Limit is >= 0 ? Limit - Used : null;
    public decimal? RemainingPercent => UsedPercent is { } value ? Math.Clamp(100 - value, 0, 100) : null;
    public double Fill => UsedPercent is { } value ? (double)Math.Clamp(value / 100, 0, 1) : 0;
    public CapacityState Capacity(decimal low = 30, decimal critical = 10) => RemainingPercent switch
    {
        null => CapacityState.Unknown, <= 0 => CapacityState.Exhausted,
        var left when left <= critical => CapacityState.Critical,
        var left when left <= low => CapacityState.Low, _ => CapacityState.Healthy
    };
}

public sealed record ResetCredit(string Id, string Status, DateTimeOffset? ExpiresAt, string? Title = null);
public sealed record ResetReserve(int? AvailableCount, IReadOnlyList<ResetCredit>? Credits)
{
    public bool HasPartialDetails => AvailableCount is > 0 && (Credits is null || Credits.Count < AvailableCount);
    public DateTimeOffset? EarliestExpiry(DateTimeOffset now) => Credits?
        .Where(c => c.Status == "available" && c.ExpiresAt > now).Select(c => c.ExpiresAt).Min();
}

public sealed record QuotaSnapshot(string AccountId, string Subject, string Scope, DateTimeOffset ObservedAt,
    IReadOnlyList<QuotaWindow> Windows, ResetReserve? Reserve, SourceHealth Health = SourceHealth.Healthy,
    string? Message = null, DateTimeOffset? ProviderDataAt = null, bool? UsageAllowed = null);

public static class ResetTime
{
    public static string Countdown(DateTimeOffset? reset, DateTimeOffset now)
    {
        if (reset is null) return "Reset time unavailable";
        var duration = reset.Value - now;
        if (duration <= TimeSpan.Zero) return "Reset due — checking";
        if (duration.TotalMinutes < 1) return "In less than a minute";
        if (duration.TotalDays >= 1) return $"In {(int)duration.TotalDays}d {duration.Hours}h";
        return duration.TotalHours >= 1 ? $"In {(int)duration.TotalHours}h {duration.Minutes}m" : $"In {duration.Minutes}m";
    }
}
