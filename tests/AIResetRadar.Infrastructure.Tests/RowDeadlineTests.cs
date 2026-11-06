using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class RowDeadlineTests
{
    [Theory]
    [InlineData("OpenAI", "primary", 300, 6, "available", 1, false)]
    [InlineData("OpenAI", "secondary", 10080, 6, "available", 1, true)]
    [InlineData("GitHub Copilot", "copilot/monthly", null, null, "available", 1, true)]
    [InlineData("Claude", "weekly", null, 1, "available", 1, false)]
    [InlineData("OpenAI", "secondary", 10080, 1, "available", 2, false)]
    [InlineData("OpenAI", "secondary", 10080, 6, "claimed", 1, false)]
    [InlineData("OpenAI", "secondary", 10080, 6, "available", -1, false)]
    [InlineData("OpenAI", "secondary", 10080, null, "available", null, false)]
    public void SelectsDeadlineForEachWindow(string provider, string id, int? minutes, int? refreshDays,
        string status, int? expiryDays, bool isExpiry)
    {
        var now = DateTimeOffset.UnixEpoch;
        DateTimeOffset? refresh = refreshDays is int r ? now.AddDays(r) : null;
        DateTimeOffset? expiry = expiryDays is int e ? now.AddDays(e) : null;
        var window = new QuotaWindow(id, id, UsageUnit.Percent, 10, 100, refresh, minutes);
        var account = new ProviderAccount("a", provider, "Personal", "s", "scope");
        var snapshot = new QuotaSnapshot("a", "s", "scope", now, [window],
            new ResetReserve(1, [new("credit", status, expiry)]));
        var row = new QuotaRow(account, snapshot, window);
        Assert.Equal((isExpiry ? expiry : refresh, isExpiry), ResetCountdown.Select(row, now));
        if (isExpiry) Assert.Equal((refresh, false), ResetCountdown.Select(row, expiry!.Value));
    }
}
