namespace AIResetRadar.Core;
public sealed record RadarSettings(bool Notifications = true, bool Sound = false, bool Privacy = false,
    bool QuietHours = false, int QuietStartHour = 22, int QuietEndHour = 8, decimal LowThreshold = 30,
    decimal CriticalThreshold = 10, bool LaunchAtSignIn = false, bool CloseToTray = true, IReadOnlyList<AlertRule>? Rules = null,
    string TimeZoneId = TimeZones.DefaultId, AccountSortMode AccountSort = AccountSortMode.NextReset,
    IReadOnlyList<string>? AccountOrder = null)
{
    public bool HasValidThresholds => Valid(LowThreshold, CriticalThreshold) && (Rules ?? []).All(r =>
    {
        var effective = RuleFor(r.AccountId, r.WindowId); return Valid(effective.LowThreshold!.Value, effective.CriticalThreshold!.Value);
    });
    static bool Valid(decimal low, decimal critical) => critical >= 0 && critical < low && low <= 100;
    public AlertRule RuleFor(string accountId, string? windowId = null)
    {
        var account = Rules?.FirstOrDefault(r => r.AccountId == accountId && r.WindowId is null) ?? new(accountId);
        var window = windowId is null ? account : Rules?.FirstOrDefault(r => r.AccountId == accountId && r.WindowId == windowId) ?? account;
        return window with { LowThreshold = window.LowThreshold ?? account.LowThreshold ?? LowThreshold,
            CriticalThreshold = window.CriticalThreshold ?? account.CriticalThreshold ?? CriticalThreshold };
    }
}

public sealed record AlertRule(string AccountId, string? WindowId = null, decimal? LowThreshold = null,
    decimal? CriticalThreshold = null, bool Low = true, bool Critical = true, bool Exhausted = true, bool Restored = true, bool Expiry = true);
