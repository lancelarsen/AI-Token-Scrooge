using AIResetRadar.Core;
using AIResetRadar.Infrastructure;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class DevinUsageTests
{
    static readonly DateTimeOffset Observed = new(2026, 12, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("example.devinenterprise.com")]
    [InlineData("https://example.devinenterprise.com/org/research")]
    [InlineData("https://example.devinenterprise.com/auth/login?redirect=/private")]
    public void KeepsOnlyTenantOrigin(string url) =>
        Assert.Equal("https://example.devinenterprise.com/", DevinClient.Workspace(url).AbsoluteUri);

    [Theory]
    [InlineData("https://example.devinenterprise.com.evil.invalid")]
    [InlineData("https://a@work.devinenterprise.com")]
    [InlineData("https://work.devinenterprise.com:444")]
    [InlineData("http://work.devinenterprise.com")]
    public void RejectsUntrustedOrigins(string url) => Assert.Throws<InvalidDataException>(() => DevinClient.Workspace(url));

    [Fact]
    public void KeepsOverageAndResolvesJanuaryResetUsingBrowserZone()
    {
        var reading = DevinUsage.Parse("sample", ["1,001.5", "900", "1/16 2:00 AM", "America/Chicago"], Observed);
        Assert.Equal(1001.5m, reading.Used);
        Assert.Equal(900m, reading.Limit);
        Assert.Equal(new DateTimeOffset(2027, 1, 16, 8, 0, 0, TimeSpan.Zero), reading.Reset);
    }

    [Theory]
    [InlineData("", "America/Chicago")]
    [InlineData("1/16 2:00 AM", "Unavailable")]
    [InlineData("5/16 2:00 AM", "America/Chicago")]
    public void PartialReadingsPreserveZeroAndUnknowns(string reset, string zone)
    {
        var reading = DevinUsage.Parse("sample", ["0", "", reset, zone], Observed);
        Assert.Equal(0m, reading.Used);
        Assert.Null(reading.Limit);
        Assert.Null(reading.Reset);
    }

    [Theory]
    [InlineData("Loading")]
    [InlineData("1,2,34")]
    [InlineData("-1")]
    public void MalformedUsageCannotBecomeZero(string value) =>
        Assert.Throws<ProviderAccessException>(() => DevinUsage.Parse("sample", [value, "100", "", ""], Observed));
}
