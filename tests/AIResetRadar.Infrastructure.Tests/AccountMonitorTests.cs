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
        public Action<string>? BeforeSave;
        public Action? AfterRemove;
        public bool FailSaveAfterWrite, FailRemove;
        public Task SaveAsync(string id, string value) { BeforeSave?.Invoke(id); Values[id] = value; if (FailSaveAfterWrite) throw new IOException("Synthetic write failure"); return Task.CompletedTask; }
        public Task<string?> ReadAsync(string id) => Task.FromResult(Values.GetValueOrDefault(id));
        public void Remove(string id) { if (FailRemove) throw new IOException("Synthetic removal failure"); Values.Remove(id); AfterRemove?.Invoke(); }
    }
    sealed class Journal : ICredentialLifecycleJournal
    {
        public readonly HashSet<string> Pending = [];
        public bool FailStage;
        public Task StageCredentialAsync(string provider, string id, CancellationToken ct = default, CredentialProfileLocation profileLocation = CredentialProfileLocation.LegacyDataDirectory)
        { if (FailStage) throw new IOException("Synthetic journal failure"); Pending.Add(id); return Task.CompletedTask; }
        public Task CompleteCredentialCleanupAsync(string id, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Pending.Remove(id); return Task.CompletedTask; }
    }

    [Fact] public async Task SignInJournalsBeforeWritingAndCleanupCompletesAfterDeletion()
    {
        var journal = new Journal(); var secrets = new Secrets { BeforeSave = id => Assert.Contains(id, journal.Pending) };
        using var http = new HttpClient(new Handler());
        var monitor = new AccountMonitor(Path.GetTempPath(), secrets, new(http, TimeProvider.System), TimeProvider.System, journal: journal);
        var result = await monitor.ConnectGitHubAsync("Synthetic", "synthetic-token", 100, UsageUnit.Credits, null, default);
        Assert.Contains(result.Account.CredentialId!, journal.Pending);
        secrets.FailRemove = true;
        await Assert.ThrowsAsync<IOException>(() => monitor.DisconnectAsync(result.Account));
        Assert.Contains(result.Account.CredentialId!, journal.Pending);
        secrets.FailRemove = false;
        await monitor.DisconnectAsync(result.Account);
        Assert.Empty(secrets.Values); Assert.Empty(journal.Pending);
    }

    [Fact] public async Task JournalFailurePreventsAnySecretWrite()
    {
        var journal = new Journal { FailStage = true }; var secrets = new Secrets();
        using var http = new HttpClient(new Handler());
        var monitor = new AccountMonitor(Path.GetTempPath(), secrets, new(http, TimeProvider.System), TimeProvider.System, journal: journal);
        await Assert.ThrowsAsync<IOException>(() => monitor.ConnectGitHubAsync("Synthetic", "synthetic-token", 100, UsageUnit.Credits, null, default));
        Assert.Empty(secrets.Values);
    }

    [Fact] public async Task CompletedRemovalClearsJournalEvenIfCallerCancelsAfterDeletion()
    {
        using var cancellation = new CancellationTokenSource();
        var journal = new Journal(); var secrets = new Secrets { AfterRemove = cancellation.Cancel };
        using var http = new HttpClient(new Handler());
        var monitor = new AccountMonitor(Path.GetTempPath(), secrets, new(http, TimeProvider.System), TimeProvider.System, journal: journal);
        var result = await monitor.ConnectGitHubAsync("Synthetic", "synthetic-token", 100, UsageUnit.Credits, null, default);
        await monitor.DisconnectAsync(result.Account, cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Empty(secrets.Values); Assert.Empty(journal.Pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSecretWriteCleansUpOrKeepsDurableIntent(bool removalFails)
    {
        var journal = new Journal(); var secrets = new Secrets { FailSaveAfterWrite = true, FailRemove = removalFails };
        using var http = new HttpClient(new Handler());
        var monitor = new AccountMonitor(Path.GetTempPath(), secrets, new(http, TimeProvider.System), TimeProvider.System, journal: journal);
        await Assert.ThrowsAsync<IOException>(() => monitor.ConnectGitHubAsync("Synthetic", "synthetic-token", 100, UsageUnit.Credits, null, default));
        Assert.Equal(removalFails ? 1 : 0, journal.Pending.Count);
        Assert.Equal(removalFails ? 1 : 0, secrets.Values.Count);
    }

    [Fact] public async Task UnknownProviderCannotStartCliOrCompleteCleanup()
    {
        var journal = new Journal(); var id = Guid.NewGuid().ToString("N"); journal.Pending.Add(id);
        using var http = new HttpClient(new Handler());
        var monitor = new AccountMonitor(Path.GetTempPath(), new Secrets(), new(http, TimeProvider.System), TimeProvider.System, journal: journal);
        var account = new ProviderAccount(id, "Unknown", "", "", "", CredentialId: id);
        await Assert.ThrowsAsync<InvalidDataException>(() => monitor.RefreshAsync(account, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => monitor.DisconnectAsync(account));
        Assert.Contains(id, journal.Pending);
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
