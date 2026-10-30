using AIResetRadar.App.Presentation;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class HorizonViewportTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-29T09:49:00-05:00");
    static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    [Fact]
    public void OpensAtTodayAndPansBackSevenDaysThroughTheSameNavigator()
    {
        var view = new HorizonViewport(Now, Now.AddDays(4), Central, 980);
        Assert.True(view.FollowingToday);
        Close(DateTimeOffset.Parse("2026-10-29T00:00:00-05:00"), view.VisibleStart);
        Assert.Equal(7, view.VisibleDays, 6);
        view.ScrollTo(0);
        Assert.False(view.FollowingToday);
        Close(Now.AddDays(-7), view.VisibleStart);
        Close(Now, view.VisibleEnd);
        Assert.Equal(0, view.Overview(980).Left);
    }

    [Fact]
    public void ShortFutureRangeStillKeepsTodayAtTheFront()
    {
        var view = new HorizonViewport(Now, Now.AddHours(2), Central, 980);
        Close(DateTimeOffset.Parse("2026-10-29T00:00:00-05:00"), view.VisibleStart);
        Assert.Equal(Now.AddHours(2), view.LastEventAt);
        Assert.True(view.EndAt > view.LastEventAt);
        Assert.Equal(view.MaximumScrollOffset, view.ScrollOffset, 6);
    }

    [Fact]
    public void RetainsFarFutureEventAndClampsNavigatorAtBothEnds()
    {
        var final = Now.AddDays(45);
        var view = new HorizonViewport(Now, final, Central, 980);
        Assert.Equal(final, view.LastEventAt);
        Assert.True(view.PositionAt(final) > view.ViewportWidth + view.ScrollOffset);
        view.CenterFromOverview(-500, 980);
        Assert.Equal(0, view.ScrollOffset);
        view.CenterFromOverview(2000, 980);
        Assert.Equal(view.MaximumScrollOffset, view.ScrollOffset);
        Assert.True(view.VisibleEnd >= final);
        var thumb = view.Overview(980);
        Assert.Equal(980, thumb.Left + thumb.Width, 6);
    }

    [Fact]
    public void OverviewDragAndEventDotsUseTheSameMapping()
    {
        var view = new HorizonViewport(Now, Now.AddDays(35), Central, 980);
        var start = view.ScrollOffset;
        var eventAt = Now.AddDays(4);
        Assert.Equal(view.PositionAt(eventAt) / view.ContentWidth * 620, view.OverviewPositionAt(eventAt, 620), 8);
        view.DragOverview(start, -40, 620);
        Assert.Equal(start - 40 / 620d * view.ContentWidth, view.ScrollOffset, 8);
        Assert.False(view.FollowingToday);
    }

    [Fact]
    public void RefreshFollowsTodayUntilTheUserBrowsesAnotherDate()
    {
        var view = new HorizonViewport(Now, Now.AddDays(12), Central, 980);
        view.Update(Now.AddHours(2), Now.AddDays(12), Central, 980);
        Close(DateTimeOffset.Parse("2026-10-29T00:00:00-05:00"), view.VisibleStart);
        view.ScrollTo(view.PositionAt(Now.AddDays(-3)));
        var browsing = view.VisibleStart;
        view.Update(Now.AddHours(3), Now.AddDays(20), Central, 980);
        Close(browsing, view.VisibleStart);
        Assert.False(view.FollowingToday);
        view.FollowToday();
        Close(DateTimeOffset.Parse("2026-10-29T00:00:00-05:00"), view.VisibleStart);
    }

    [Fact]
    public void ResizeKeepsTheBrowsedDateAndNarrowDaysRemainReadable()
    {
        var view = new HorizonViewport(Now, Now.AddDays(12), Central, 980);
        view.ScrollTo(view.PositionAt(Now.AddDays(-3)));
        var browsing = view.VisibleStart;
        view.Update(Now, Now.AddDays(12), Central, 400);
        Assert.Equal(HorizonViewport.MinimumDayWidth, view.PixelsPerDay);
        Assert.Equal(5, view.VisibleDays);
        Close(browsing, view.VisibleStart);
        view.Update(Now, Now.AddDays(12), Central, 1400);
        Close(browsing, view.VisibleStart);
        Assert.True(view.VisibleDays > 12);
    }

    [Fact]
    public void ChangingTimeZonePreservesTheBrowsedInstant()
    {
        var view = new HorizonViewport(Now, Now.AddDays(12), Central, 980);
        view.ScrollTo(view.PositionAt(Now.AddDays(-2)));
        var browsing = view.VisibleStart;
        view.Update(Now, Now.AddDays(12), TimeZoneInfo.Utc, 980);
        Close(browsing, view.VisibleStart);
        Assert.Equal(TimeSpan.Zero, view.VisibleStart.Offset);
    }

    [Fact]
    public void ModeChangesReturnToTodayAndFutureOnlyFitsAvailableDays()
    {
        var view = new HorizonViewport(Now, Now.AddDays(4), Central, 980);
        view.ScrollTo(0);
        view.Update(Now, Now.AddDays(4), Central, 980, false);
        Assert.False(view.HistoryEnabled);
        Assert.True(view.FollowingToday);
        Assert.Equal(0, view.ScrollOffset);
        Assert.Equal(980, view.ContentWidth, 6);
        Assert.Equal(new DateOnly(2026, 10, 29), view.FirstDate);
        view.Update(Now, Now.AddDays(4), Central, 980, true);
        Close(DateTimeOffset.Parse("2026-10-29T00:00:00-05:00"), view.VisibleStart);
        Assert.True(view.FollowingToday);
    }

    [Fact]
    public void FutureOnlyStillScrollsWhenTheHorizonIsLong()
    {
        var view = new HorizonViewport(Now, Now.AddDays(35), Central, 980, false);
        Assert.Equal(80, view.PixelsPerDay);
        Assert.Equal(36 * 80, view.ContentWidth);
        view.ScrollTo(view.MaximumScrollOffset);
        Assert.True(view.VisibleEnd > view.LastEventAt);
    }

    [Theory]
    [InlineData("2026-11-02T09:49:00-06:00", 2026, 10, 31)]
    [InlineData("2026-03-09T09:49:00-05:00", 2026, 3, 7)]
    public void DayTicksStayEquallySpacedAcrossDst(string nowText, int year, int month, int day)
    {
        var now = DateTimeOffset.Parse(nowText);
        var view = new HorizonViewport(now, now.AddDays(5), Central, 980);
        var start = new DateOnly(year, month, day);
        Assert.Equal(view.PixelsPerDay, view.PositionAt(start.AddDays(1)) - view.PositionAt(start), 6);
        Assert.Equal(view.PixelsPerDay, view.PositionAt(start.AddDays(2)) - view.PositionAt(start.AddDays(1)), 6);
        view.ScrollTo(0);
        Close(now, view.VisibleEnd);
    }

    [Fact]
    public void RepeatedDstHourRemainsMonotonicAndRoundTrips()
    {
        var now = DateTimeOffset.Parse("2026-11-02T09:49:00-06:00");
        var view = new HorizonViewport(now, now.AddDays(5), Central, 980);
        var first = DateTimeOffset.Parse("2026-11-01T01:30:00-05:00");
        var second = DateTimeOffset.Parse("2026-11-01T01:30:00-06:00");
        Assert.True(view.PositionAt(first) < view.PositionAt(second));
        Close(first, view.DateAt(view.PositionAt(first)));
        Close(second, view.DateAt(view.PositionAt(second)));
    }

    [Fact]
    public void EmptyDataAndTransientZeroWidthHaveFiniteGeometry()
    {
        var view = new HorizonViewport(Now, null, Central, 0);
        Assert.Equal(Now, view.LastEventAt);
        Assert.True(double.IsFinite(view.ContentWidth));
        Assert.True(double.IsFinite(view.ScrollOffset));
        view.ScrollTo(double.NaN);
        view.CenterFromOverview(10, 0);
        view.DragOverview(0, 10, double.NaN);
        Assert.True(double.IsFinite(view.ScrollOffset));
        Assert.Equal((0d, 0d), view.Overview(0));
    }

    [Fact]
    public void RollingRetentionClampsOnlyWhenTheBrowsedDateLeavesTheRange()
    {
        var view = new HorizonViewport(Now, Now.AddDays(12), Central, 980);
        view.ScrollTo(0);
        view.Update(Now.AddDays(1), Now.AddDays(12), Central, 980);
        Assert.Equal(0, view.ScrollOffset);
        Close(Now.AddDays(-6), view.VisibleStart);
        Assert.False(view.FollowingToday);
    }

    [Fact]
    public void HistoryCannotScrollBeforeFirstRecordedSample()
    {
        var first = Now.AddDays(-3).AddHours(-2);
        var view = new HorizonViewport(Now, Now.AddDays(8), Central, 980, earliestHistory: first);
        view.ScrollTo(-10000);
        Close(first, view.VisibleStart);
        Assert.Equal(first, view.StartAt);
        view.FollowToday();
        Close(DateTimeOffset.Parse("2026-10-29T00:00:00-05:00"), view.VisibleStart);
    }

    [Fact]
    public void LongerRangeCompactsDaysUntilReadableMinimum()
    {
        var shortRange = new HorizonViewport(Now, Now.AddDays(4), Central, 980);
        var longer = new HorizonViewport(Now, Now.AddDays(9), Central, 980);
        var longest = new HorizonViewport(Now, Now.AddDays(35), Central, 980);
        Assert.True(longer.PixelsPerDay < shortRange.PixelsPerDay);
        Assert.Equal(HorizonViewport.MinimumDayWidth, longest.PixelsPerDay);
        Assert.True(longer.VisibleEnd >= longer.LastEventAt);
    }

    [Fact]
    public void NoRecordedHistoryStopsAtToday()
    {
        var view = new HorizonViewport(Now, Now.AddDays(8), Central, 980, earliestHistory: Now);
        view.ScrollTo(0);
        Close(Now, view.VisibleStart);
        Assert.True(double.IsFinite(view.Overview(980).Width));
    }

    [Theory]
    [InlineData(9, 980)]
    [InlineData(16, 980)]
    [InlineData(23, 980)]
    [InlineData(16, 480)]
    public void FutureDayAnchorsStayEquallySpacedIncludingToday(int hour, double width)
    {
        var now = new DateTimeOffset(2026, 10, 29, hour, 15, 0, TimeSpan.FromHours(-5));
        // Crosses the DST transition and reproduces the afternoon Today-clamping regression.
        var view = new HorizonViewport(now, now.AddDays(8), Central, width, earliestHistory: now.AddDays(-3));
        foreach (var history in new[] { true, false })
        {
            view.Update(now, now.AddDays(8), Central, width, history, now.AddDays(-3));
            foreach (var offset in new[] { view.ScrollOffset, 0, view.MaximumScrollOffset })
            {
                view.ScrollTo(offset);
                for (var date = view.Today; date < view.LastDate; date = date.AddDays(1))
                    Assert.Equal(view.PixelsPerDay,
                        view.FutureDayPosition(date.AddDays(1)) - view.FutureDayPosition(date), 6);
            }
            view.ScrollTo(view.MaximumScrollOffset);
            Assert.InRange(view.FutureDayPosition(view.LastDate) - view.ScrollOffset, 0, width);
            view.FollowToday();
            Assert.Equal(view.PixelsPerDay / 2, view.FutureDayPosition(view.Today) - view.ScrollOffset, 6);
        }
    }

    [Fact]
    public void EventTimeSharesCalendarDayScaleWithHeaders()
    {
        var view = new HorizonViewport(Now, Now.AddDays(8), Central, 980);
        var day = new DateOnly(2026, 10, 28);
        var morning = DateTimeOffset.Parse("2026-10-28T06:00:00-05:00");
        var afternoon = DateTimeOffset.Parse("2026-10-28T18:00:00-05:00");
        var center = view.FutureDayPosition(day);
        Assert.Equal(center - view.PixelsPerDay / 4, view.PositionAt(morning), 6);
        Assert.Equal(center + view.PixelsPerDay / 4, view.PositionAt(afternoon), 6);
    }

    static void Close(DateTimeOffset expected, DateTimeOffset actual) =>
        Assert.True((expected - actual).Duration() < TimeSpan.FromMilliseconds(1), $"Expected {expected:O}; got {actual:O}");
}
