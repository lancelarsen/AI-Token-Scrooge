using System.Collections.Concurrent;
using AIResetRadar.Core;
using Xunit;
using static AIResetRadar.Infrastructure.Tests.GitHubDeviceFlowTests;
namespace AIResetRadar.Infrastructure.Tests;

public class GitHubCredentialTests
{
    sealed class Secrets : ISecretStore
    {
        public ConcurrentDictionary<string, string> Values = new();
        public Func<string, bool>? Reject;
        public Task SaveAsync(string id, string value) { if (Reject?.Invoke(value) == true) throw new IOException("Test storage failure"); Values[id] = value; return Task.CompletedTask; }
        public Task<string?> ReadAsync(string id) => Task.FromResult(Values.GetValueOrDefault(id));
        public void Remove(string id) => Values.TryRemove(id, out _);
    }
    static ProviderAccount Account(string key = "credential", string subject = "42") => new("account", "GitHub Copilot", "Personal", subject, "personal", CredentialId: key);
    static GitHubAppTokens Expiring(Clock clock) => new("Iv1.original", "old-access", "old-refresh", clock.Now.AddSeconds(30), clock.Now.AddDays(30));

    [Fact] public async Task ConcurrentReadersRotateOnceUsingTheOriginalClientAndRemovalDoesNotResurrectCredentials()
    {
        var clock = new Clock(); var secrets = new Secrets(); var rotations = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (r, ct) => {
            if (r.RequestUri!.Host == "api.github.com") return Json("""{"id":42,"login":"sample"}""");
            Assert.Contains("client_id=Iv1.original", await r.Content!.ReadAsStringAsync(ct));
            Interlocked.Increment(ref rotations); entered.SetResult(); await release.Task;
            return Json(Tokens);
        }));
        var manager = new GitHubCredentialManager(secrets, new(http, clock), new(http, clock), clock);
        await manager.SaveAppAsync("credential", "42", Expiring(clock));
        var first = manager.ReadAccessTokenAsync(Account(), default); await entered.Task;
        var second = manager.ReadAccessTokenAsync(Account(), default); release.SetResult();
        Assert.Equal(new[] { "ghu_test-access", "ghu_test-access" }, await Task.WhenAll(first, second)); Assert.Equal(1, rotations);
        await manager.RemoveAsync("credential", default); Assert.Empty(secrets.Values);
        await Assert.ThrowsAsync<ProviderAccessException>(() => manager.ReadAccessTokenAsync(Account(), default));
    }

    [Fact] public async Task TransientIdentityFailureKeepsReplacementAndNeverRetriesConsumedRefreshToken()
    {
        var clock = new Clock(); var secrets = new Secrets(); var rotations = 0; var checks = 0;
        using var http = new HttpClient(new Handler((r, _) => {
            if (r.RequestUri!.Host == "github.com") { rotations++; return Task.FromResult(Json(Tokens)); }
            if (++checks == 1) throw new HttpRequestException("Test offline");
            return Task.FromResult(Json("""{"id":42,"login":"sample"}"""));
        }));
        var manager = new GitHubCredentialManager(secrets, new(http, clock), new(http, clock), clock);
        await manager.SaveAppAsync("credential", "42", Expiring(clock));
        await Assert.ThrowsAsync<HttpRequestException>(() => manager.ReadAccessTokenAsync(Account(), default));
        var restarted = new GitHubCredentialManager(secrets, new(http, clock), new(http, clock), clock);
        Assert.Equal("ghu_test-access", await restarted.ReadAccessTokenAsync(Account(), default)); Assert.Equal(1, rotations);
    }

    [Fact] public async Task FailedRotationSaveCanRetryInMemoryAndCrashStateRequiresReconnect()
    {
        var clock = new Clock(); var secrets = new Secrets(); var rotations = 0;
        using var http = new HttpClient(new Handler((r, _) => { if (r.RequestUri!.Host == "github.com") rotations++; return Task.FromResult(Json(r.RequestUri.Host == "github.com" ? Tokens : """{"id":42,"login":"sample"}""")); }));
        var manager = new GitHubCredentialManager(secrets, new(http, clock), new(http, clock), clock);
        await manager.SaveAppAsync("credential", "42", Expiring(clock));
        secrets.Reject = value => value.Contains("ghu_test-access");
        await Assert.ThrowsAsync<ProviderAccessException>(() => manager.ReadAccessTokenAsync(Account(), default));
        var restarted = new GitHubCredentialManager(secrets, new(http, clock), new(http, clock), clock);
        await Assert.ThrowsAsync<ProviderAccessException>(() => restarted.ReadAccessTokenAsync(Account(), default));
        secrets.Reject = null;
        Assert.Equal("ghu_test-access", await manager.ReadAccessTokenAsync(Account(), default)); Assert.Equal(1, rotations);
    }

    [Fact] public async Task CancellationAfterExchangeStillSavesTheReplacement()
    {
        var clock = new Clock(); var secrets = new Secrets(); using var cancel = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((r, _) => Task.FromResult(Json(r.RequestUri!.Host == "github.com" ? Tokens : """{"id":42,"login":"sample"}"""))));
        var manager = new GitHubCredentialManager(secrets, new(http, clock), new(http, clock), clock);
        await manager.SaveAppAsync("credential", "42", Expiring(clock));
        secrets.Reject = value => { if (value.Contains("ghu_test-access")) cancel.Cancel(); return false; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.ReadAccessTokenAsync(Account(), cancel.Token));
        secrets.Reject = null;
        var restarted = new GitHubCredentialManager(secrets, new(http, clock), new(http, clock), clock);
        Assert.Equal("ghu_test-access", await restarted.ReadAccessTokenAsync(Account(), default));
    }

    [Fact] public async Task WrongSubjectNeverReturnsAReplacementAndRemovalWaitsForRenewal()
    {
        var clock = new Clock(); var secrets = new Secrets();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (r, _) => { if (r.RequestUri!.Host == "github.com") { entered.SetResult(); await release.Task; return Json(Tokens); } return Json("""{"id":99,"login":"other"}"""); }));
        var manager = new GitHubCredentialManager(secrets, new(http, clock), new(http, clock), clock);
        await manager.SaveAppAsync("credential", "42", Expiring(clock));
        var read = manager.ReadAccessTokenAsync(Account(), default); await entered.Task;
        var remove = manager.RemoveAsync("credential", default); Assert.False(remove.IsCompleted); release.SetResult();
        var error = await Assert.ThrowsAsync<ProviderAccessException>(() => read); Assert.Equal(SourceHealth.AuthenticationRequired, error.Health);
        await remove; Assert.Empty(secrets.Values);
    }

    [Fact] public async Task LegacyTokensAndIndependentAccountsRemainSeparate()
    {
        var clock = new Clock(); var secrets = new Secrets(); secrets.Values["legacy"] = "legacy-pat";
        using var http = new HttpClient(new Handler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected network request")));
        var manager = new GitHubCredentialManager(secrets, new(http, clock), new(http, clock), clock);
        await manager.SaveAppAsync("fresh", "84", Expiring(clock) with { ExpiresAt = clock.Now.AddHours(8) });
        Assert.Equal("legacy-pat", await manager.ReadAccessTokenAsync(Account("legacy"), default));
        Assert.Equal("old-access", await manager.ReadAccessTokenAsync(Account("fresh", "84"), default));
        await Assert.ThrowsAsync<ProviderAccessException>(() => manager.ReadAccessTokenAsync(Account("fresh", "42"), default));
        await manager.RemoveAsync("fresh", default); Assert.Equal("legacy-pat", secrets.Values["legacy"]);
    }
}
