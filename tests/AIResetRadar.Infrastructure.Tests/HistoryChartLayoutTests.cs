using AIResetRadar.App.Presentation;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class HistoryChartLayoutTests
{
    [Fact]
    public void AccountColorsDoNotDependOnEnumerationOrderOrOtherAccounts()
    {
        var first = HistoryChartLayout.ColorForAccount("openai-plus");
        var second = HistoryChartLayout.ColorForAccount("openai-pro");
        _ = HistoryChartLayout.ColorForAccount("new-account");
        Assert.Equal(first, HistoryChartLayout.ColorForAccount("openai-plus"));
        Assert.Equal(second, HistoryChartLayout.ColorForAccount("openai-pro"));
        Assert.NotEqual(first, second);
        Assert.Matches("^#[A-F0-9]{6}$", first);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(236)]
    public void ClusteredEndpointLabelsRemainInsideGraphAndSeparate(float desired)
    {
        var positions = HistoryChartLayout.PlaceLabels([desired, desired, desired, desired], 30, 236);
        Assert.All(positions, p => Assert.InRange(p, 30, 236));
        Assert.All(positions.Zip(positions.Skip(1)), p => Assert.True(p.Second - p.First >= 25));
    }

    [Fact]
    public void ChartGrowsForEveryFutureEventRowAndManyEndpointLabels()
    {
        Assert.Equal(298, HistoryChartLayout.Height(4, 2));
        Assert.Equal(HistoryChartLayout.Height(3, 0), HistoryChartLayout.Height(1, 0));
        Assert.Equal(226, HistoryChartLayout.Height(3, 0));
        Assert.Equal(72, HistoryChartLayout.Height(5, 0) - HistoryChartLayout.Height(4, 0));
        Assert.True(HistoryChartLayout.Height(4, 4) >= 81 + 4 * 76);
        Assert.True(HistoryChartLayout.Height(20, 2) >= 20 * 25 + 70);
        Assert.Empty(HistoryChartLayout.PlaceLabels([], 30, 236));
    }
}
