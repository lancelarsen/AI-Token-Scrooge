namespace AIResetRadar.Core;
public static class HistoryRetention
{
    public const int Days = 30;
    public static DateTimeOffset Cutoff(DateTimeOffset now) => now.AddDays(-Days);
    public static AlertEvent[] PruneAlerts(IEnumerable<AlertEvent> alerts, DateTimeOffset now) => alerts.Where(a => a.CreatedAt >= Cutoff(now)).ToArray();
}
