using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;
public class HistoryDemoShapeTests
{
    [Fact]
    public void DemoHasAllFourCapacityStatesAndUnevenHistory()
    {
        var now = DateTimeOffset.Parse("2026-10-31T18:00:00Z");
        var state = new DashboardState();
        state.LoadDemo(now, historyDemo: true);
        var remaining = state.Snapshots.Values.SelectMany(s => s.Windows).Select(w => w.RemainingPercent!.Value).ToArray();
        Assert.Contains(remaining, x => x > 50);
        Assert.Contains(remaining, x => x <= 50 && x > 30);
        Assert.Contains(remaining, x => x <= 30 && x > 10);
        Assert.Contains(remaining, x => x <= 10);
        var series = HistoryDemo.Build(state.Accounts.ToArray(), state.Snapshots, now, TimeZoneInfo.Utc);
        foreach (var line in series)
        {
            var pairs = line.Points.Zip(line.Points.Skip(1)).ToArray();
            Assert.True(pairs.Count(p => p.First.RemainingPercent == p.Second.RemainingPercent) > pairs.Length / 2);
            Assert.Contains(pairs, p => p.First.RemainingPercent > p.Second.RemainingPercent);
        }
    }
}
