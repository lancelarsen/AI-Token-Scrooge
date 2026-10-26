using System.Net;
using System.Text;
using AIResetRadar.Core;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;
public class AccountMonitorTests
{
    sealed class Secrets : ISecretStore
    {
        public readonly Dictionary<string, string> Values = [];
        public Task SaveAsync(string id, string value) { Values[id] = value; return Task.CompletedTask; }
        public Task<string?> ReadAsync(string id) => Task.FromResult(Values.GetValueOrDefault(id));
        public void Remove(string id) => Values.Remove(id);
    }
    sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(request.RequestUri!.AbsolutePath == "/user" ? "{\"id\":42,\"login\":\"sample\"}" : "{\"usageItems\":[]}", Encoding.UTF8, "application/json") });
    }
    [Fact] public async Task ReconnectStagesANewSecretAndPreservesThePausedAccount()
    {
        var secrets = new Secrets(); secrets.Values["old-profile"] = "old-token";
        using var http = new HttpClient(new Handler());
        var monitor = new AccountMonitor(Path.GetTempPath(), secrets, new(http, TimeProvider.System), TimeProvider.System);
        var original = new ProviderAccount(Guid.NewGuid().ToString("N"), "GitHub Copilot", "Work", "42", "personal", Enabled: false, CredentialId: "old-profile");
        var result = await monitor.ConnectGitHubAsync("Work", "new-token", 100, UsageUnit.Credits, null, default, original);
        Assert.Equal(original.Id, result.Account.Id); Assert.False(result.Account.Enabled);
        Assert.NotEqual(original.CredentialId, result.Account.CredentialId);
        Assert.Equal("old-token", secrets.Values["old-profile"]);
        Assert.Equal("new-token", secrets.Values[result.Account.CredentialId!]);
        await monitor.DisconnectAsync(result.Account);
        Assert.Equal("old-token", secrets.Values["old-profile"]);
        Assert.False(secrets.Values.ContainsKey(result.Account.CredentialId!));
    }

    [Fact] public async Task BrowserReconnectRejectsAnotherIdentityWithoutReplacingTheSavedConnection()
    {
        var secrets = new Secrets(); secrets.Values["original"] = "original-token";
        using var http = new HttpClient(new Handler());
        var monitor = new AccountMonitor(Path.GetTempPath(), secrets, new(http, TimeProvider.System), TimeProvider.System);
        var original = new ProviderAccount("account", "GitHub Copilot", "Work", "99", "personal", CredentialId: "original");
        var tokens = new GitHubAppTokens("Iv1.test", "new-access", "new-refresh", DateTimeOffset.UtcNow.AddHours(8), DateTimeOffset.UtcNow.AddMonths(6));
        await Assert.ThrowsAsync<ProviderAccessException>(() => monitor.ConnectGitHubWithBrowserAsync("Work", tokens, 100, UsageUnit.Credits, default, original));
        Assert.Single(secrets.Values); Assert.Equal("original-token", secrets.Values["original"]);
    }

    [Fact] public async Task BrowserReconnectStagesAProtectedEnvelopeAndPreservesExistingPreferences()
    {
        var secrets = new Secrets(); secrets.Values["original"] = "original-token";
        using var http = new HttpClient(new Handler());
        var monitor = new AccountMonitor(Path.GetTempPath(), secrets, new(http, TimeProvider.System), TimeProvider.System);
        var original = new ProviderAccount("account", "GitHub Copilot", "Work", "42", "personal", Enabled: false, CredentialId: "original");
        var tokens = new GitHubAppTokens("Iv1.test", "new-access", "new-refresh", DateTimeOffset.UtcNow.AddHours(8), DateTimeOffset.UtcNow.AddMonths(6));
        var result = await monitor.ConnectGitHubWithBrowserAsync("Work", tokens, 100, UsageUnit.Credits, default, original);
        Assert.Equal(original.Id, result.Account.Id); Assert.False(result.Account.Enabled);
        Assert.NotEqual(original.CredentialId, result.Account.CredentialId);
        Assert.StartsWith("github-app:v1:", secrets.Values[result.Account.CredentialId!]);
        Assert.Equal("original-token", secrets.Values["original"]);
        Assert.DoesNotContain("new-access", System.Text.Json.JsonSerializer.Serialize(result));
        Assert.DoesNotContain("new-refresh", System.Text.Json.JsonSerializer.Serialize(result));
        await monitor.DisconnectAsync(result.Account); Assert.Single(secrets.Values);
    }
}
