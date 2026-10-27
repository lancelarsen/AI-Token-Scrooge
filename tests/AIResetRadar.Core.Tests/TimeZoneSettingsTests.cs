using System.Text.Json;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Core.Tests;

public class TimeZoneSettingsTests
{
    [Fact]
    public void OlderSettingsDefaultToCentralAndSelectionRoundTrips()
    {
        var older = JsonSerializer.Deserialize<RadarSettings>("{\"Sound\":true}")!;
        Assert.Equal(TimeZones.DefaultId, older.TimeZoneId);
        Assert.True(older.Sound);
        var updated = older with { TimeZoneId = "Pacific Standard Time" };
        Assert.Equal(updated, JsonSerializer.Deserialize<RadarSettings>(JsonSerializer.Serialize(updated)));
        Assert.Equal(TimeZones.DefaultId, TimeZones.Resolve("no-such-zone").Id);
    }

    [Fact]
    public void CentralUsesCorrectOffsetAcrossDaylightSavingTransition()
    {
        var zone = TimeZones.Resolve(TimeZones.DefaultId);
        var before = DateTimeOffset.Parse("2026-11-01T06:30:00Z");
        var after = before.AddHours(1);
        Assert.Equal(TimeSpan.FromHours(-5), TimeZoneInfo.ConvertTime(before, zone).Offset);
        Assert.Equal(TimeSpan.FromHours(-6), TimeZoneInfo.ConvertTime(after, zone).Offset);
        Assert.Equal("CDT", TimeZones.Abbreviation(before, zone));
        Assert.Equal("CST", TimeZones.Abbreviation(after, zone));
    }

    [Fact]
    public void DisplayDateFollowsSelectedZoneWhileCountdownUsesElapsedTime()
    {
        var reset = DateTimeOffset.Parse("2026-10-27T02:00:00Z");
        Assert.Equal("Mon, Oct 26 9:00 PM", TimeZones.Format(reset, TimeZones.Resolve(TimeZones.DefaultId)));
        Assert.Equal("Tue, Oct 27 2:00 AM", TimeZones.Format(reset, TimeZoneInfo.Utc));
        Assert.Equal("Resets: 4h 59m", ResetTime.Summary(reset, reset.AddHours(-4).AddMinutes(-59)));
        Assert.Equal("Reset time unavailable", ResetTime.Summary(null, reset));
        Assert.Equal("Reset due — checking", ResetTime.Summary(reset, reset));
    }
}
