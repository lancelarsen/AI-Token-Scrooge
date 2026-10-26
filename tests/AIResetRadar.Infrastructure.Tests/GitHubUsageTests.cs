using System.Net;
using System.Text;
using AIResetRadar.Core;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;
public class GitHubUsageTests
{
    sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
    static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PersonalAllowanceUsesVerifiedProviderDataOrClearlyMarkedManualFallback(bool available)
    {
        using var http = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/user" => Json("""{"id":42,"login":"sample"}"""),
            "/copilot_internal/user" => available ? Json("""{"token_based_billing":true,"quota_snapshots":{"premium_interactions":{"entitlement":20000,"unlimited":false}}}""") : new(HttpStatusCode.Forbidden),
            _ => Json("""{"usageItems":[{"unitType":"credits","grossQuantity":228.8}]}""")
        }));
        var result = await new GitHubUsageClient(http, TimeProvider.System).ReadAsync(new("a", "GitHub Copilot", "Personal", "42", "personal", Allowance: 100), "test-token", default);
        var window = Assert.Single(result.Windows);
        Assert.Equal(available ? 20000m : 100m, window.Limit);
        Assert.Equal(available ? AllowanceProvenance.Provider : AllowanceProvenance.Manual, window.AllowanceSource);
        Assert.Equal(228.8m, window.Used);
    }

    [Theory]
    [InlineData("true", "false", "\"20000\"", UsageUnit.Credits, 20000)]
    [InlineData("false", "false", "300", UsageUnit.Requests, 300)]
    [InlineData("true", "false", "0", UsageUnit.Credits, 0)]
    [InlineData("true", "true", "0", UsageUnit.Credits, -1)]
    [InlineData("true", "false", "-3", UsageUnit.Credits, -1)]
    [InlineData("false", "false", "300", UsageUnit.Credits, -1)]
    [InlineData("null", "false", "300", UsageUnit.Requests, -1)]
    [InlineData("true", "false", "\"bad\"", UsageUnit.Credits, -1)]
    public void AllowanceRequiresMatchingUnitsAndFiniteAllocation(string billing, string unlimited, string amount, UsageUnit unit, int expected)
    {
        using var data = System.Text.Json.JsonDocument.Parse("{\"token_based_billing\":" + billing + ",\"quota_snapshots\":{\"premium_interactions\":{\"entitlement\":" + amount + ",\"unlimited\":" + unlimited + "}}}");
        Assert.Equal(expected < 0 ? (decimal?)null : expected, ProviderMapping.GitHubAllowance(data.RootElement, unit));
    }

    [Fact] public async Task TokenIdentityMismatchCannotAttachBillingData()
    {
        using var http = new HttpClient(new Handler(_ => Json("""{"id":99,"login":"other"}""")));
        var client = new GitHubUsageClient(http, TimeProvider.System);
        var error = await Assert.ThrowsAsync<ProviderAccessException>(() => client.ReadAsync(new("a", "GitHub Copilot", "Work", "42", "personal"), "test-token", default));
        Assert.Equal(SourceHealth.AuthenticationRequired, error.Health);
    }

    [Fact] public async Task OrganizationReportsAreFilteredToTheAuthenticatedUser()
    {
        using var http = new HttpClient(new Handler(request => {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            if (request.RequestUri.AbsolutePath == "/user") return Json("""{"id":42,"login":"sample-user"}""");
            Assert.Contains("organizations/example/settings/billing/ai_credit/usage", request.RequestUri.AbsolutePath);
            Assert.Contains("user=sample-user", request.RequestUri.Query);
            return Json("""{"usageItems":[]}""");
        }));
        var client = new GitHubUsageClient(http, TimeProvider.System);
        var result = await client.ReadAsync(new("a", "GitHub Copilot", "Work", "42", "organization:example"), "test-token", default);
        Assert.Equal("organization:example", result.Scope);
    }

    [Fact] public async Task RateLimited403IsNotMistakenForMissingPermission()
    {
        using var http = new HttpClient(new Handler(_ => {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
            response.Headers.Add("X-RateLimit-Remaining", "0"); response.Headers.RetryAfter = new(TimeSpan.FromMinutes(2)); return response;
        }));
        var error = await Assert.ThrowsAsync<ProviderAccessException>(() => new GitHubUsageClient(http, TimeProvider.System).IdentifyAsync("test-token", default));
        Assert.Equal(SourceHealth.Stale, error.Health); Assert.True(error.RetryAt > DateTimeOffset.UtcNow.AddMinutes(1));
    }

    sealed class UnknownLengthContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) => await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"id\":42,\"login\":\"sample\",\"padding\":\"" + new string('a', 5_000_001) + "\"}"));
    }
    [Fact] public async Task ReportSizeIsBoundedEvenWithoutAContentLength()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new UnknownLengthContent() }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUsageClient(http, TimeProvider.System).IdentifyAsync("test-token", default));
    }
}
