namespace AIResetRadar.Core;
public static class HistoryRetention
{
    public const int Days = 90;
    public static DateTimeOffset Cutoff(DateTimeOffset now, int days = Days) => now.AddDays(-Math.Clamp(days, 1, 365));
    public static AlertEvent[] PruneAlerts(IEnumerable<AlertEvent> alerts, DateTimeOffset now, int days = Days) => alerts.Where(a => a.CreatedAt >= Cutoff(now, days)).ToArray();
}
