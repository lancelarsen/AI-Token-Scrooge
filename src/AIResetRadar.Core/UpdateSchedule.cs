namespace AIResetRadar.Core;

public static class UpdateSchedule
{
    public static bool IsDue(bool enabled, DateTimeOffset now, DateTimeOffset? lastSuccess, DateTimeOffset retryAfter) =>
        enabled && now >= retryAfter && (lastSuccess is null || now < lastSuccess || now - lastSuccess >= TimeSpan.FromHours(8));
}
