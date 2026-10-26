using AIResetRadar.App.Presentation;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;
public class HorizonTimelineTests
{
    [Fact]
    public void DashboardRetainsFurthestEventAndDropsDisabledAccountEvents()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var state = new DashboardState();
        state.LoadDemo(now);
        var events = state.Events(now).ToArray();
        Assert.Equal(6, events.Length);
        Assert.Equal(now.AddDays(9), events[^1].At);
        var days = HorizonTimeline.Build(events, now, TimeZoneInfo.Utc);
        Assert.Equal(10, days.Length);
        Assert.Equal(new DateOnly(2026, 11, 3), days[^1].Date);

        var account = state.Accounts.Single(a => a.Id == events[^1].AccountId);
        state.Accounts[state.Accounts.IndexOf(account)] = account with { Enabled = false };
        var remaining = state.Events(now).ToArray();
        Assert.Equal(2, remaining.Length);
        Assert.DoesNotContain(remaining, e => e.AccountId == account.Id);
        Assert.Equal(new DateOnly(2026, 11, 1), HorizonTimeline.Build(remaining, now, TimeZoneInfo.Utc)[^1].Date);
    }

    [Fact]
    public void IncludesEmptyDaysAndAllEventsThroughFurthestDate()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var events = Enumerable.Range(0, 7).Select(i => new HorizonEvent("account", "Reset", "Weekly", now.AddHours(i + 1), false))
            .Append(new HorizonEvent("account", "Expiry", "Credit", now.AddDays(4), true)).ToArray();
        var days = HorizonTimeline.Build(events, now, TimeZoneInfo.Utc);
        Assert.Equal(5, days.Length);
        Assert.True(days[0].IsToday);
        Assert.Equal(7, days[0].Events.Length);
        Assert.Empty(days[1].Events);
        Assert.True(Assert.Single(days[^1].Events).IsExpiry);
        Assert.Equal(new DateOnly(2026, 10, 29), days[^1].Date);
    }
    [Fact]
    public void LocalCalendarDaysStayContiguousAcrossDstAndExcludePastEvents()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var now = DateTimeOffset.Parse("2026-10-31T23:00:00-05:00");
        var events = new[] {
            new HorizonEvent("a", "Past", "", now.AddMinutes(-1), false),
            new HorizonEvent("a", "Reset", "", DateTimeOffset.Parse("2026-11-02T00:00:00-06:00"), false) };
        var days = HorizonTimeline.Build(events, now, zone);
        Assert.Equal(new[] { new DateOnly(2026,10,31), new DateOnly(2026,11,1), new DateOnly(2026,11,2) }, days.Select(d=>d.Date));
        Assert.Empty(days[0].Events);
        Assert.Single(days[^1].Events);
        Assert.Single(HorizonTimeline.Build([], now, zone));
    }
}
