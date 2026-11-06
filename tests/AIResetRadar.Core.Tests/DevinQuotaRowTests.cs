using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Core.Tests;

public class DevinQuotaRowTests
{
    [Theory]
    [InlineData(43, 90, "48% used", "52% left")]
    [InlineData(101, 100, "101% used", "0% left")]
    [InlineData(12, null, "Usage unavailable", "Left unavailable")]
    public void ShowsPercentageLabels(int used, int? limit, string usedText, string leftText)
    {
        var account = new ProviderAccount("a", "Devin", "Work", "user", "tenant");
        var window = new QuotaWindow("devin/monthly", "Monthly ACUs", UsageUnit.Acus, used, limit, null);
        var snapshot = new QuotaSnapshot("a", "user", "tenant", DateTimeOffset.UnixEpoch, [window], null);
        var row = new QuotaRow(account, snapshot, window);
        Assert.Equal(usedText, row.UsedText);
        Assert.Equal(leftText, row.LeftText);
    }
}
