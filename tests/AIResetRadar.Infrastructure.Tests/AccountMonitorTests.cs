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
}
