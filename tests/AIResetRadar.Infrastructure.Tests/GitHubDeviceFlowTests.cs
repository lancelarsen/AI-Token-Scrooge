using System.Net;
using System.Text;
using AIResetRadar.Core;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;

public class GitHubDeviceFlowTests
{
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        public readonly List<double> Delays = [];
        public override DateTimeOffset GetUtcNow() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Now.UtcTicks;
        public Task Delay(TimeSpan delay, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Delays.Add(delay.TotalSeconds); Now += delay; return Task.CompletedTask; }
    }
    internal sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request, ct);
    }
    internal static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    const string Challenge = """{"device_code":"test-device-code-123456789","user_code":"ABCD-EFGH","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""";
    internal const string Tokens = """{"access_token":"ghu_test-access","refresh_token":"ghr_test-refresh","expires_in":28800,"refresh_token_expires_in":15897600,"token_type":"bearer","scope":""}""";

    [Fact] public async Task ApprovalRespectsPendingAndSlowDownWithoutSendingAClientSecret()
    {
        var clock = new Clock(); var polls = 0;
        using var http = new HttpClient(new Handler(async (request, ct) => {
            Assert.Equal("https", request.RequestUri!.Scheme); Assert.Equal("github.com", request.RequestUri.Host);
            Assert.Empty(request.RequestUri.Query);
            var form = await request.Content!.ReadAsStringAsync(ct);
            Assert.DoesNotContain("client_secret", form);
            if (request.RequestUri.AbsolutePath == "/login/device/code") { Assert.Contains("scope=offline_access", form); return Json(Challenge); }
            Assert.Equal("/login/oauth/access_token", request.RequestUri.AbsolutePath);
            return Json(++polls switch { 1 => """{"error":"authorization_pending"}""", 2 => """{"error":"slow_down","interval":12}""", _ => Tokens });
        }));
        var flow = new GitHubDeviceFlow(http, clock, clock.Delay);
        var challenge = await flow.BeginAsync("Iv1.test", default);
        var tokens = await flow.WaitAsync(challenge, default);
        Assert.Equal(new double[] { 5, 5, 12 }, clock.Delays);
        Assert.Equal("Iv1.test", tokens.ClientId); Assert.Equal(clock.Now.AddHours(8), tokens.ExpiresAt);
        Assert.DoesNotContain("test-access", tokens.ToString()); Assert.DoesNotContain("test-device", challenge.ToString());
    }

    [Theory]
    [InlineData("https://github.com.evil.invalid/login/device")]
    [InlineData("https://github.com/login/device?code=untrusted")]
    [InlineData("http://github.com/login/device")]
    public async Task RejectsUnexpectedVerificationAddresses(string address)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(Challenge.Replace("https://github.com/login/device", address)))));
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubDeviceFlow(http, new Clock()).BeginAsync("Iv1.test", default));
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("expired_token")]
    public async Task DenialOrExpiryStopsPollingAndDoesNotReflectProviderText(string error)
    {
        var clock = new Clock(); var polls = 0;
        using var http = new HttpClient(new Handler((r, _) => Task.FromResult(Json(r.RequestUri!.AbsolutePath.EndsWith("/code") ? Challenge : ++polls > 0 ? "{\"error\":\"" + error + "\",\"error_description\":\"private-value\"}" : Tokens))));
        var flow = new GitHubDeviceFlow(http, clock, clock.Delay);
        var challenge = await flow.BeginAsync("Iv1.test", default);
        var failure = await Assert.ThrowsAsync<ProviderAccessException>(() => flow.WaitAsync(challenge, default));
        Assert.Equal(1, polls); Assert.DoesNotContain("private-value", failure.Message);
    }

    [Fact] public async Task CancellationDoesNotPollAndExpiredChallengeIsNotSubmitted()
    {
        var clock = new Clock(); var requests = 0;
        using var http = new HttpClient(new Handler((_, _) => { requests++; return Task.FromResult(Json(Challenge)); }));
        var flow = new GitHubDeviceFlow(http, clock, clock.Delay); var challenge = await flow.BeginAsync("Iv1.test", default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flow.WaitAsync(challenge, new CancellationToken(true)));
        clock.Now += TimeSpan.FromMinutes(16);
        await Assert.ThrowsAsync<ProviderAccessException>(() => flow.WaitAsync(challenge, default));
        Assert.Equal(1, requests);
    }

    [Fact] public async Task RedirectAndOversizedResponsesAreRejected()
    {
        using var redirect = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect))));
        await Assert.ThrowsAsync<ProviderAccessException>(() => new GitHubDeviceFlow(redirect, new Clock()).BeginAsync("Iv1.test", default));
        using var large = new HttpClient(new Handler((_, _) => Task.FromResult(Json(new string('x', 65537)))));
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubDeviceFlow(large, new Clock()).BeginAsync("Iv1.test", default));
    }
}
