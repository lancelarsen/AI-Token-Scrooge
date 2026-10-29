namespace AIResetRadar.Core;

public enum ReminderSeverity { Info, Warning, Critical }
public sealed record TimedReminder(string Id, int Amount, string Unit, ReminderSeverity Severity)
{
    public double Hours => Unit == "Days" ? (double)Amount * 24 : Amount;
    public bool IsValid => Amount > 0 && Unit is "Days" or "Hours" && Enum.IsDefined(Severity);
    public static IReadOnlyList<TimedReminder> Defaults(bool expiration) =>
        (expiration ? new[] { 240, 120, 72, 48, 24, 12, 6, 3, 1 } : new[] { 120, 72, 48, 24, 12, 6, 3, 1 })
        .Select(h => new TimedReminder($"default-{h}", h >= 24 ? h / 24 : h, h >= 24 ? "Days" : "Hours",
            h >= 120 ? ReminderSeverity.Info : h >= 48 ? ReminderSeverity.Warning : ReminderSeverity.Critical)).ToArray();
    // Catch up with the nearest due reminder, rather than flooding users with every missed milestone.
    public static TimedReminder? Due(IReadOnlyList<TimedReminder> reminders, DateTimeOffset at, DateTimeOffset now) =>
        at <= now ? null : reminders.Where(r => r.IsValid && (at - now).TotalHours <= r.Hours)
            .OrderBy(r => r.Hours).ThenByDescending(r => r.Severity).FirstOrDefault();
}
