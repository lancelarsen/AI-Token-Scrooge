using AIResetRadar.Core;

namespace AIResetRadar.App.Presentation;

public enum HistoryEventKind { ScheduledRefresh, ReportedExpiration }

public sealed record HistoryPoint(DateTimeOffset At, decimal RemainingPercent, string WindowId,
    bool StartsSegment, DateTimeOffset? PreviousObservedAt = null, decimal? ReplenishedFrom = null, bool InterpolatedFromPrevious = false);

public sealed record HistoryEvent(string AccountId, string? WindowId, string Id, DateTimeOffset At,
    HistoryEventKind Kind, DateTimeOffset ReportedAt, string Detail);

public sealed record HistorySeries(ProviderAccount Account, string Period, string PlanLabel,
    HistoryPoint[] Points, HistoryEvent[] Events, string? Status = null)
{
    public HistoryPoint? Latest => Points.LastOrDefault();
}

public sealed record HistoryWindowSelection(QuotaWindow? Window, bool IsAmbiguous = false);

/// <summary>Projects recorded provider observations without manufacturing usage or reset events.</summary>
public static class UsageHistory
{
    public static bool SupportsProvider(string provider) => provider is "OpenAI" or "Claude" or "GitHub Copilot";

    public static bool IsEligible(string provider, QuotaWindow window) => provider switch
    {
        "OpenAI" => window.DurationMinutes == 10080,
        "Claude" => window.DurationMinutes == 10080 || window.Id == "weekly" && window.DurationMinutes is null,
        "GitHub Copilot" => window.Id == "copilot/monthly",
        _ => false
    };

    public static HistoryWindowSelection SelectWindow(string provider, IEnumerable<QuotaWindow> windows)
    {
        var eligible = windows.Where(w => IsEligible(provider, w)).ToArray();
        if (eligible.Length < 2) return new(eligible.SingleOrDefault());
        // A provider may add unrelated buckets. Prefer only its known canonical product bucket.
        var canonical = eligible.Where(w => provider switch
        {
            "OpenAI" => w.Id.StartsWith("codex/", StringComparison.Ordinal),
            "Claude" => w.Id == "weekly",
            _ => false
        }).ToArray();
        return canonical.Length == 1 ? new(canonical[0]) : new(null, true);
    }

    public static HistorySeries Build(ProviderAccount account, IEnumerable<QuotaSnapshot> snapshots,
        DateTimeOffset from, DateTimeOffset through, TimeSpan? pollingInterval = null)
    {
        if (through < from) throw new ArgumentOutOfRangeException(nameof(through));
        var period = account.Provider == "GitHub Copilot" ? "Monthly" : "Weekly";
        if (!SupportsProvider(account.Provider)) return new(account, period, "Plan unavailable", [], [], "History Unavailable");
        var ordered = snapshots.Where(s => s.AccountId == account.Id && s.ObservedAt <= through)
            .OrderBy(s => s.ObservedAt).GroupBy(s => s.ObservedAt).Select(g => g.Last()).ToArray();
        var prior = ordered.LastOrDefault(s => s.ObservedAt < from);
        var interval = pollingInterval ?? TimeSpan.FromMinutes(account.Provider == "OpenAI" ? 2 : 5);
        var cadenceTicks = Math.Clamp(interval.Ticks, 0, TimeSpan.MaxValue.Ticks / 3);
        var maxGap = TimeSpan.FromTicks(Math.Max(TimeSpan.FromMinutes(15).Ticks, cadenceTicks * 3));
        var relevant = ordered.Where(s => s.ObservedAt >= from || ReferenceEquals(s, prior)).ToArray();
        var current = ordered.LastOrDefault(s => s.BelongsTo(account));
        var plan = PlanLabels.Display(account.Provider, current?.PlanType);
        List<HistoryPoint> points = [];
        List<HistoryEvent> events = [];
        Dictionary<string, HistoryEvent> pendingRefreshes = [];
        Dictionary<string, HistoryEvent> expirations = [];
        QuotaWindow? previousWindow = null;
        QuotaSnapshot? previousSnapshot = null;
        HistoryPoint? previousPoint = null;
        bool missedObservation = false;
        string? status = null;

        foreach (var snapshot in relevant)
        {
            if (!snapshot.BelongsTo(account))
            {
                previousPoint = null;
                previousWindow = null;
                previousSnapshot = null;
                status = "Waiting for Fresh Usage";
                continue;
            }
            if (snapshot.Health is not (SourceHealth.Healthy or SourceHealth.Demo))
            {
                missedObservation = true;
                status = "Waiting for Fresh Usage";
                continue;
            }
            var selected = SelectWindow(account.Provider, snapshot.Windows);
            var window = selected.Window;
            status = selected.IsAmbiguous ? $"Multiple {period} Limits Reported" : window is null ? $"No {period} Limit Reported" : null;

            if (window?.ResetsAt is { } at)
            {
                if (pendingRefreshes.TryGetValue(window.Id, out var pending) && pending.At != at)
                {
                    // Before its due time, a changed timestamp revises the same schedule.
                    // Once it was due, retain it as a reported schedule, never as proof of redemption.
                    if (snapshot.ObservedAt >= pending.At) events.Add(pending);
                }
                pendingRefreshes[window.Id] = new(account.Id, window.Id, $"refresh:{window.Id}:{at.ToUnixTimeMilliseconds()}", at,
                    HistoryEventKind.ScheduledRefresh, snapshot.ObservedAt, $"Scheduled {period} Quota Refresh");
            }
            foreach (var credit in snapshot.Reserve?.Credits ?? [])
            {
                if (credit.Status == "available" && credit.ExpiresAt is { } expiry)
                    expirations[credit.Id] = new(account.Id, null, $"credit:{credit.Id}", expiry,
                        HistoryEventKind.ReportedExpiration, snapshot.ObservedAt, "Last Reported Earned Reset Expiration");
                else if (credit.Status is "used" or "redeemed" && expirations.TryGetValue(credit.Id, out var known) && snapshot.ObservedAt < known.At)
                    expirations.Remove(credit.Id);
            }

            if (window?.RemainingPercent is not { } remaining)
            {
                missedObservation = true;
                status ??= "Remaining Capacity Unavailable";
                continue;
            }
            // An old anchor is useful only when a nearby real sample can connect to it.
            if (snapshot.ObservedAt < from && from - snapshot.ObservedAt > maxGap) continue;
            var starts = previousPoint is null || previousWindow is null || previousSnapshot is null ||
                window.Id != previousWindow.Id ||
                window.Unit != previousWindow.Unit || window.Limit != previousWindow.Limit ||
                window.AllowanceSource != previousWindow.AllowanceSource || snapshot.PlanType != previousSnapshot.PlanType ||
                !string.Equals(snapshot.Scope, previousSnapshot.Scope,
                    account.Provider == "GitHub Copilot" ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            var estimated = !starts && (missedObservation || snapshot.ObservedAt - previousPoint!.At > maxGap);
            var replenished = !starts && !estimated && remaining > previousPoint!.RemainingPercent;
            var point = new HistoryPoint(snapshot.ObservedAt, remaining, window.Id, starts,
                starts ? null : previousPoint!.At, replenished ? previousPoint!.RemainingPercent : null, estimated);
            points.Add(point);
            previousPoint = point;
            previousWindow = window;
            previousSnapshot = snapshot;
            missedObservation = false;
        }

        events.AddRange(pendingRefreshes.Values);
        events.AddRange(expirations.Values);
        var historicalEvents = events.Where(e => e.At >= from && e.At <= through)
            .GroupBy(e => e.Id).Select(g => g.OrderBy(e => e.ReportedAt).Last())
            .OrderBy(e => e.At).ThenBy(e => e.Id, StringComparer.Ordinal).ToArray();
        if (points.Count == 0) status ??= "No Recorded History Yet";
        else if (through - points[^1].At > maxGap) status ??= "Waiting for Fresh Usage";
        return new(account, period, plan, points.ToArray(), historicalEvents, status);
    }

    /// <summary>Keeps original observations for hover details; this is only a drawing reduction.</summary>
    public static HistoryPoint[] Downsample(IReadOnlyList<HistoryPoint> points,
        Func<DateTimeOffset, double> projectX, double minX, double maxX)
    {
        if (points.Count == 0 || maxX < minX) return [];
        // Build produces chronological points and the shared calendar projection is monotonic.
        // Search the viewport first so a drag does not project every off-screen sample each frame.
        int Bound(double x, bool inclusive)
        {
            var low = 0; var high = points.Count;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                var projected = projectX(points[middle].At);
                if (projected < x || !inclusive && projected == x) low = middle + 1;
                else high = middle;
            }
            return low;
        }
        var first = Math.Max(0, Bound(minX, true) - 1);
        var last = Math.Min(points.Count - 1, Bound(maxX, false));
        if (last - first < 2) return Enumerable.Range(first, last - first + 1).Select(i => points[i]).ToArray();
        HashSet<int> retained = [];
        double bucket = double.NaN;
        var bucketFirst = first; var bucketLast = first; var minimum = first; var maximum = first;
        void KeepBucket()
        {
            retained.Add(bucketFirst); retained.Add(bucketLast); retained.Add(minimum); retained.Add(maximum);
        }
        for (var i = first; i <= last; i++)
        {
            var x = Math.Floor(projectX(points[i].At));
            if (x != bucket)
            {
                if (!double.IsNaN(bucket)) KeepBucket();
                bucket = x; bucketFirst = i; minimum = i; maximum = i;
            }
            bucketLast = i;
            if (points[i].RemainingPercent < points[minimum].RemainingPercent) minimum = i;
            if (points[i].RemainingPercent > points[maximum].RemainingPercent) maximum = i;
            // Keep both endpoints of estimated spans, and the next measured sample,
            // so pixel reduction cannot disguise the estimated interval.
            if (!points[i].StartsSegment && !points[i].ReplenishedFrom.HasValue &&
                !points[i].InterpolatedFromPrevious &&
                !(i > first && points[i - 1].InterpolatedFromPrevious)) continue;
            retained.Add(i);
            if (i > first) retained.Add(i - 1);
        }
        KeepBucket();
        return retained.Order().Select(i => points[i]).ToArray();
    }
}
