namespace AIResetRadar.Core;

/// <summary>Identical measured state at both ends; no intermediate usage is invented.</summary>
public sealed record HistoryInterval(long Key, QuotaSnapshot First, QuotaSnapshot Last, long ObservationCount, long MaximumGapTicks = 0)
{
    public IEnumerable<QuotaSnapshot> Endpoints()
    {
        yield return First;
        if (Last.ObservedAt > First.ObservedAt)
            yield return Last with { HistoryContinuousFrom = First.ObservedAt, HistoryMaximumGapTicks = MaximumGapTicks };
    }
}

public sealed record HistoryPage(long Revision, IReadOnlyList<HistoryInterval> Intervals);
