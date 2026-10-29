namespace AIResetRadar.Core;

public sealed record AlertEvent(string Key, string AccountId, string? WindowId, string Title, string Detail,
    DateTimeOffset CreatedAt, bool Delivered = false, ReminderSeverity? Severity = null, string? ReminderId = null, bool IsRefreshReminder = false);

public static class AlertEvaluator
{
    public static bool IsEnabled(AlertEvent alert, RadarSettings settings)
    {
        if (alert.ReminderId is { } id)
            return (alert.IsRefreshReminder || settings.RuleFor(alert.AccountId).Expiry) &&
                (alert.IsRefreshReminder ? settings.EffectiveRefreshReminders : settings.EffectiveResetReminders).Any(r => r.Id == id && r.IsValid);
        return alert.WindowId is not { } windowId ||
            !(alert.Key.EndsWith("/low", StringComparison.Ordinal) || alert.Key.EndsWith("/critical", StringComparison.Ordinal) || alert.Key.EndsWith("/exhausted", StringComparison.Ordinal)) ||
            settings.CapacityAlertsEnabled(alert.AccountId, windowId);
    }
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
            var priorWindow = previous?.Windows.FirstOrDefault(w => w.Id == window.Id);
            if (window.ResetsAt is { } refresh && TimedReminder.Due(settings.EffectiveRefreshReminders, refresh, now) is { } reminder &&
                !IsMovingEstimate(priorWindow?.ResetsAt, refresh, previous?.ObservedAt, reminder, settings, now))
                alerts.Add(Reminder(account, window.Id, window.Id, refresh, reminder, true, settings, now, window.Name));
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
                    $"{window.RemainingPercent:0.#}% left Â· {account.Provider} / {account.Nickname}", now));
            }
            if (rule.Restored && before?.Capacity() == CapacityState.Exhausted && window.RemainingPercent > 0 && current.UsageAllowed == true)
                alerts.Add(new($"{account.Id}/{window.Id}/{period}/restored", account.Id, window.Id, $"{window.Name} is available again",
                    $"Provider confirmed capacity Â· {account.Provider} / {account.Nickname}", now));
        }
        foreach (var credit in settings.RuleFor(account.Id).Expiry && current.Reserve?.AvailableCount != 0 ? current.Reserve?.Credits ?? [] : [])
        {
            if (credit.Status != "available" || credit.ExpiresAt is not { } expiry || expiry <= now) continue;
            if (TimedReminder.Due(settings.EffectiveResetReminders, expiry, now) is { } reminder)
                alerts.Add(Reminder(account, null, credit.Id, expiry, reminder, false, settings, now, "Earned Reset"));
        }
        return alerts;
    }
    // Some providers recompute a rolling reset estimate on each read. Do not repeat the
    // same milestone solely because that future timestamp moved by a few minutes.
    static bool IsMovingEstimate(DateTimeOffset? before, DateTimeOffset at, DateTimeOffset? observed,
        TimedReminder reminder, RadarSettings settings, DateTimeOffset now) =>
        before is { } prior && prior != at && prior > now && observed is { } read &&
        Math.Abs((at - prior).TotalMinutes) <= 10 &&
        TimedReminder.Due(settings.EffectiveRefreshReminders, prior, read)?.Id == reminder.Id;
    static AlertEvent Reminder(ProviderAccount account, string? window, string target, DateTimeOffset at,
        TimedReminder reminder, bool refresh, RadarSettings settings, DateTimeOffset now, string name) =>
        new($"{account.Id}/{(refresh ? "refresh" : "credit")}/{target}/{at.ToUnixTimeSeconds()}/{reminder.Id}", account.Id, window,
            $"{reminder.Severity} Alert · {(refresh ? "Quota Refresh" : "Reset Expiration")}",
            $"{name} · {ResetTime.Countdown(at, now)} · {TimeZones.Format(at, TimeZones.Resolve(settings.TimeZoneId), "ddd, MMM d h:mm tt")} · {account.Nickname}",
            now, Severity: reminder.Severity, ReminderId: reminder.Id, IsRefreshReminder: refresh);
}
