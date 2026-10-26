using AIResetRadar.Infrastructure;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class ClaudeUsageTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 26, 18, 0, 0, TimeSpan.FromHours(-5));
    [Fact] public void ReadsDistinctWindowsAndProviderTimezone()
    {
        var windows = ClaudeUsageParser.Parse("Current session\n 12% used\nResets 8:30pm (America/Chicago)\nCurrent week (all models)\n 31% used\nResets Oct 28, 12am (America/Chicago)\nExtra usage\n", Now);
        Assert.Equal(2, windows.Count);
        Assert.Equal(12m, windows[0].Used);
        Assert.Equal(Now.AddHours(2.5), windows[0].ResetsAt);
        Assert.Equal(31m, windows[1].Used);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.FromHours(-5)), windows[1].ResetsAt);
    }
    [Fact] public void MissingSessionDoesNotBorrowWeeklyMeter()
    {
        var windows = ClaudeUsageParser.Parse("Current session\nStarts with your first message\nCurrent week (all models)\n0% used\n", Now);
        Assert.Single(windows); Assert.Equal("weekly", windows[0].Id); Assert.Null(windows[0].ResetsAt);
    }
    [Theory]
    [InlineData("101% used")]
    [InlineData("-1% used")]
    [InlineData("1e2% used")]
    [InlineData("loading")]
    public void RejectsInvalidOrMissingUsage(string text) => Assert.Empty(ClaudeUsageParser.Parse("Current session\n" + text, Now));
    [Fact] public void NeverGuessesUnknownTimezoneOrDistantReset()
    {
        Assert.Null(ClaudeUsageParser.Parse("Current session\n0% used\nResets 8pm (Unknown/Zone)", Now)[0].ResetsAt);
        Assert.Null(ClaudeUsageParser.Parse("Current session\n0% used\nResets 11am (America/Chicago)", Now)[0].ResetsAt);
    }
    [Fact] public void ReadsCurrentCliSingleLineFormat()
    {
        var windows = ClaudeUsageParser.Parse("Current session: 0% used · resets Oct 26, 8:30pm (America/Chicago)\nCurrent week (all models): 0% used · resets Oct 28, 12am (America/Chicago)", Now);
        Assert.Equal(2, windows.Count); Assert.Equal(Now.AddHours(2.5), windows[0].ResetsAt);
    }
}
