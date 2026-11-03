using System.Text.Json;
using Xunit;

namespace AIResetRadar.Core.Tests;

public sealed class BuildVersionTests
{
    [Theory]
    [InlineData("v2026.11.3.001", "v2026.11.3.002")]
    [InlineData("v2026.11.3.009", "v2026.11.3.010")]
    [InlineData("v2026.11.3.100", "v2026.11.4.001")]
    [InlineData("v2026.12.31.999", "v2027.01.1.001")]
    public void VersionsSortNumerically(string before, string after)
    {
        Assert.True(BuildVersion.TryParse(before, out var old));
        Assert.True(BuildVersion.TryParse(after, out var next));
        Assert.True(next.CompareTo(old) > 0);
        Assert.Equal(after, next.ToString());
    }

    [Theory]
    [InlineData("2026.2.29.001")]
    [InlineData("2026.13.1.001")]
    [InlineData("2026.11.3.-1")]
    [InlineData("2026.11.3.65535")]
    [InlineData("preview-16-1")]
    [InlineData("vv2026.11.3.001")]
    [InlineData("2026.11.3.1.0")]
    public void InvalidVersionsAreRejected(string text) => Assert.False(BuildVersion.TryParse(text, out _));

    [Fact]
    public void OldSettingsDefaultToChecksOnAndExplicitOffPersists()
    {
        Assert.True(JsonSerializer.Deserialize<RadarSettings>("{}")!.CheckForNewerVersions);
        Assert.False(JsonSerializer.Deserialize<RadarSettings>(JsonSerializer.Serialize(new RadarSettings(CheckForNewerVersions: false)))!.CheckForNewerVersions);
    }

    [Fact]
    public void EightHourScheduleHonorsOffRetryAndClockChanges()
    {
        var now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        Assert.True(UpdateSchedule.IsDue(true, now, null, default));
        Assert.False(UpdateSchedule.IsDue(false, now, null, default));
        Assert.False(UpdateSchedule.IsDue(true, now, now.AddHours(-7), default));
        Assert.True(UpdateSchedule.IsDue(true, now, now.AddHours(-8), default));
        Assert.False(UpdateSchedule.IsDue(true, now, null, now.AddMinutes(30)));
        Assert.True(UpdateSchedule.IsDue(true, now, now.AddHours(1), default));
    }
}
