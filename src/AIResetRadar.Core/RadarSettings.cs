namespace AIResetRadar.Core;
public sealed record RadarSettings(bool Notifications = true, bool Sound = false, bool Privacy = false,
    bool QuietHours = false, int QuietStartHour = 22, int QuietEndHour = 8, decimal LowThreshold = 30,
    decimal CriticalThreshold = 10, bool LaunchAtSignIn = true, bool CloseToTray = true, IReadOnlyList<AlertRule>? Rules = null,
    string TimeZoneId = TimeZones.DefaultId, AccountSortMode AccountSort = AccountSortMode.NextReset,
    IReadOnlyList<string>? AccountOrder = null, IReadOnlyList<QuotaAlertSection>? DisabledCapacityAlerts = null, string SoundTheme = "Ducks", double SoundVolume = 65,
    IReadOnlyList<TimedReminder>? ResetExpiryReminders = null, IReadOnlyList<TimedReminder>? QuotaRefreshReminders = null, decimal InfoThreshold = 50, bool AlertOnIcon = true)
{
    public IReadOnlyList<TimedReminder> EffectiveResetReminders => ResetExpiryReminders ?? TimedReminder.Defaults(true);
    public IReadOnlyList<TimedReminder> EffectiveRefreshReminders => QuotaRefreshReminders ?? TimedReminder.Defaults(false);
    public bool CapacityAlertsEnabled(string accountId, string windowId) =>
        !(DisabledCapacityAlerts ?? []).Any(section => section.AccountId == accountId && section.WindowId == windowId);
    public RadarSettings WithCapacityAlerts(string accountId, string windowId, bool enabled)
    {
        var disabled = (DisabledCapacityAlerts ?? []).Where(section => section.AccountId != accountId || section.WindowId != windowId).ToList();
        if (!enabled) disabled.Add(new(accountId, windowId));
        return this with { DisabledCapacityAlerts = disabled.ToArray() };
    }
    public RadarSettings WithWindowThresholds(string accountId, string windowId, decimal low, decimal critical, decimal? info = null)
    {
        if (!new AlertThresholds(low, critical, info ?? Math.Max(InfoThreshold, low)).IsValid) throw new ArgumentOutOfRangeException(nameof(low), "Use 0 ≤ critical < warning ≤ 100.");
        var stored = Rules?.FirstOrDefault(rule => rule.AccountId == accountId && rule.WindowId == windowId)
            ?? Rules?.FirstOrDefault(rule => rule.AccountId == accountId && rule.WindowId is null) ?? new AlertRule(accountId);
        var updated = stored with { WindowId = windowId, LowThreshold = low, CriticalThreshold = critical, InfoThreshold = info ?? Math.Max(InfoThreshold, low) };
        return this with { Rules = (Rules ?? []).Where(rule => rule.AccountId != accountId || rule.WindowId != windowId).Append(updated).ToArray() };
    }
    public bool HasValidThresholds => Valid(LowThreshold, CriticalThreshold) && InfoThreshold >= LowThreshold && InfoThreshold <= 100 && (Rules ?? []).All(r =>
    {
        var effective = RuleFor(r.AccountId, r.WindowId); return Valid(effective.LowThreshold!.Value, effective.CriticalThreshold!.Value) && effective.InfoThreshold >= effective.LowThreshold && effective.InfoThreshold <= 100;
    });
    static bool Valid(decimal low, decimal critical) => critical >= 0 && critical < low && low <= 100;
    public AlertRule RuleFor(string accountId, string? windowId = null)
    {
        var account = Rules?.FirstOrDefault(r => r.AccountId == accountId && r.WindowId is null) ?? new(accountId);
        var window = windowId is null ? account : Rules?.FirstOrDefault(r => r.AccountId == accountId && r.WindowId == windowId) ?? account;
        return window with { LowThreshold = window.LowThreshold ?? account.LowThreshold ?? LowThreshold,
            CriticalThreshold = window.CriticalThreshold ?? account.CriticalThreshold ?? CriticalThreshold,
            InfoThreshold = window.InfoThreshold ?? account.InfoThreshold ?? Math.Max(InfoThreshold, window.LowThreshold ?? account.LowThreshold ?? LowThreshold) };
    }
}

public sealed record QuotaAlertSection(string AccountId, string WindowId);

public sealed record AlertRule(string AccountId, string? WindowId = null, decimal? LowThreshold = null,
    decimal? CriticalThreshold = null, bool Low = true, bool Critical = true, bool Exhausted = true, bool Restored = true, bool Expiry = true, decimal? InfoThreshold = null, bool Info = true);
