namespace AIResetRadar.App.Presentation;

/// <summary>Calendar-day geometry shared by the horizon, history graph, and overview navigator.</summary>
public sealed class HorizonViewport
{
    public const int HistoryDays = 7;
    public const double MinimumDayWidth = 80;
    double startCoordinate;
    double endCoordinate;
    bool initialized;

    public HorizonViewport(DateTimeOffset now, DateTimeOffset? furthestFuture, TimeZoneInfo timeZone,
        double viewportWidth, bool historyEnabled = true, DateTimeOffset? earliestHistory = null) =>
        Update(now, furthestFuture, timeZone, viewportWidth, historyEnabled, earliestHistory);

    public DateTimeOffset Now { get; private set; }
    public DateTimeOffset StartAt { get; private set; }
    public DateTimeOffset EndAt { get; private set; }
    public DateTimeOffset LastEventAt { get; private set; }
    public DateOnly FirstDate => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(StartAt, TimeZone).DateTime);
    public DateOnly LastDate => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(LastEventAt, TimeZone).DateTime);
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, TimeZone).DateTime);
    public TimeZoneInfo TimeZone { get; private set; } = TimeZoneInfo.Utc;
    public bool HistoryEnabled { get; private set; }
    public bool FollowingToday { get; private set; } = true;
    public double ViewportWidth { get; private set; }
    public double PixelsPerDay { get; private set; }
    public double VisibleDays => ViewportWidth / PixelsPerDay;
    public double ContentWidth => (endCoordinate - startCoordinate) * PixelsPerDay;
    public double MaximumScrollOffset => Math.Max(0, ContentWidth - ViewportWidth);
    public double ScrollOffset { get; private set; }
    public double TodayPosition => PositionAt(Now);
    public DateTimeOffset VisibleStart => DateAt(ScrollOffset);
    public DateTimeOffset VisibleEnd => DateAt(ScrollOffset + ViewportWidth);

    /// <summary>Update data/layout without losing the date a person is browsing.</summary>
    public void Update(DateTimeOffset now, DateTimeOffset? furthestFuture, TimeZoneInfo timeZone,
        double viewportWidth, bool historyEnabled = true, DateTimeOffset? earliestHistory = null)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var previousLeft = initialized ? VisibleStart : now;
        var modeChanged = initialized && historyEnabled != HistoryEnabled;
        Now = now;
        TimeZone = timeZone;
        ViewportWidth = double.IsFinite(viewportWidth) ? Math.Max(1, viewportWidth) : 1;
        HistoryEnabled = historyEnabled;
        LastEventAt = furthestFuture is { } last && last > now ? last : now;
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;
        var todayStart = Midnight(Today, timeZone);
        var futureEnd = Midnight(LastDate.AddDays(1), timeZone);
        if (historyEnabled)
        {
            // Preserve the local clock time seven dates earlier. The fractional-day projection
            // below keeps DST dates equally wide while preserving the order of repeated hours.
            StartAt = ResolveLocal(localNow.AddDays(-HistoryDays), timeZone);
            if (earliestHistory is { } first && first > StartAt) StartAt = first < now ? first : now;
            startCoordinate = Coordinate(StartAt, timeZone);
            var historySpan = Coordinate(now, timeZone) - startCoordinate;
            // Fit the larger side of Today, compacting longer ranges down to readable day widths.
            var futureSpan = LastDate.DayNumber - Today.DayNumber + 1;
            PixelsPerDay = Math.Max(MinimumDayWidth, ViewportWidth / Math.Max(1, Math.Max(historySpan, futureSpan)));
            // Empty space beyond a short future range lets Today stay at the left edge.
            // It is layout space only; LastEventAt remains the actual final reported event.
            endCoordinate = Today.DayNumber + Math.Max(futureSpan, VisibleDays);
        }
        else
        {
            StartAt = todayStart;
            startCoordinate = Coordinate(todayStart, timeZone);
            endCoordinate = Coordinate(futureEnd, timeZone);
            PixelsPerDay = Math.Max(MinimumDayWidth, ViewportWidth / Math.Max(1, endCoordinate - startCoordinate));
        }
        EndAt = FromCoordinate(endCoordinate, timeZone);
        if (!initialized || modeChanged || FollowingToday) FollowToday();
        else ScrollOffset = ClampOffset(PositionAt(previousLeft));
        initialized = true;
    }

    public void FollowToday()
    {
        FollowingToday = true;
        ScrollOffset = HistoryEnabled ? ClampOffset(PositionAt(Today)) : 0;
    }

    public void ScrollTo(double offset)
    {
        if (double.IsNaN(offset)) return;
        ScrollOffset = ClampOffset(offset);
        var anchor = HistoryEnabled ? ClampOffset(PositionAt(Today)) : 0;
        FollowingToday = Math.Abs(ScrollOffset - anchor) < 0.5;
    }

    /// <summary>Unclipped content position, suitable for clipping a line at viewport edges.</summary>
    public double PositionAt(DateTimeOffset timestamp) =>
        (Coordinate(timestamp, TimeZone) - startCoordinate) * PixelsPerDay;

    public double PositionAt(DateOnly date) => (date.DayNumber - startCoordinate) * PixelsPerDay;

    /// <summary>Equal-width future day buckets, including Today; never clamp individual markers.</summary>
    public double FutureDayPosition(DateOnly date) =>
        PositionAt(date) + 0.5 * PixelsPerDay;

    public DateTimeOffset DateAt(double contentX)
    {
        if (!double.IsFinite(contentX)) contentX = 0;
        return FromCoordinate(startCoordinate + Math.Clamp(contentX, 0, ContentWidth) / PixelsPerDay, TimeZone);
    }

    public (double Left, double Width) Overview(double trackWidth)
    {
        trackWidth = SafeTrackWidth(trackWidth);
        return (ScrollOffset / ContentWidth * trackWidth, Math.Min(1, ViewportWidth / ContentWidth) * trackWidth);
    }

    public double OverviewPositionAt(DateTimeOffset timestamp, double trackWidth) =>
        PositionAt(timestamp) / ContentWidth * SafeTrackWidth(trackWidth);

    public void CenterFromOverview(double pointerX, double trackWidth)
    {
        trackWidth = SafeTrackWidth(trackWidth);
        if (trackWidth <= 0 || !double.IsFinite(pointerX)) return;
        ScrollTo(pointerX / trackWidth * ContentWidth - ViewportWidth / 2);
    }

    public void DragOverview(double initialScrollOffset, double pointerDelta, double trackWidth)
    {
        trackWidth = SafeTrackWidth(trackWidth);
        if (trackWidth <= 0 || !double.IsFinite(initialScrollOffset) || !double.IsFinite(pointerDelta)) return;
        ScrollTo(initialScrollOffset + pointerDelta / trackWidth * ContentWidth);
    }

    double ClampOffset(double offset) => Math.Clamp(offset, 0, MaximumScrollOffset);
    static double SafeTrackWidth(double width) => double.IsFinite(width) ? Math.Max(0, width) : 0;

    static double Coordinate(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
        var beginning = Midnight(date, zone);
        var end = Midnight(date.AddDays(1), zone);
        return date.DayNumber + (instant - beginning).TotalSeconds / (end - beginning).TotalSeconds;
    }

    static DateTimeOffset FromCoordinate(double coordinate, TimeZoneInfo zone)
    {
        var dayNumber = (int)Math.Floor(coordinate);
        var date = DateOnly.FromDayNumber(dayNumber);
        var beginning = Midnight(date, zone);
        var end = Midnight(date.AddDays(1), zone);
        return TimeZoneInfo.ConvertTime(beginning.AddTicks((long)Math.Round((end - beginning).Ticks * (coordinate - dayNumber))), zone);
    }

    static DateTimeOffset Midnight(DateOnly date, TimeZoneInfo zone) => ResolveLocal(date.ToDateTime(TimeOnly.MinValue), zone);

    static DateTimeOffset ResolveLocal(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        // Some time zones advance at midnight. Start the day at its first valid instant.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }
}
