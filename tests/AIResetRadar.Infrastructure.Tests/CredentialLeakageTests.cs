using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using AIResetRadar.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class CredentialLeakageTests : IDisposable
{
    const string Access = "synthetic-access-sentinel-4f162ab8";
    const string Refresh = "synthetic-refresh-sentinel-536aec70";
    readonly string folder = Path.Combine(Path.GetTempPath(), "ScroogeLeakageTests", Guid.NewGuid().ToString("N"));
    string Database => Path.Combine(folder, "data", "radar.db");

    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected-sign-in")]
    [InlineData("failed-commit")]
    public async Task BrowserCredentialsStayOutOfPersistentStateAndErrorText(string scenario)
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new LocalStore(Database);
        await store.WriteAsync("settings", new RadarSettings());
        var secrets = new WindowsSecretStore(Path.Combine(folder, "security", "Credentials"));
        using var http = new HttpClient(new Handler(scenario == "rejected-sign-in"));
        var monitor = new AccountMonitor(Path.Combine(folder, "data"), secrets,
            new GitHubUsageClient(http, TimeProvider.System), TimeProvider.System, journal: store);
        var tokens = new GitHubAppTokens("Iv1.synthetic-public-client", Access, Refresh,
            DateTimeOffset.UtcNow.AddHours(1), DateTimeOffset.UtcNow.AddDays(1));

        if (scenario == "rejected-sign-in")
        {
            var error = await Assert.ThrowsAsync<ProviderAccessException>(() =>
                monitor.ConnectGitHubWithBrowserAsync("Synthetic", tokens, 100, UsageUnit.Credits, default));
            AssertNoSentinels(Encoding.UTF8.GetBytes(error.ToString()));
            Assert.Empty((await store.LoadMonitoringStateAsync()).Accounts);
            Assert.Empty((await store.LoadMonitoringStateAsync()).Retired);
            AssertNoSentinelsInFiles();
            return;
        }

        var result = await monitor.ConnectGitHubWithBrowserAsync("Synthetic", tokens, 100, UsageUnit.Credits, default);
        var savedSecret = await secrets.ReadAsync(result.Account.CredentialId!);
        Assert.Contains(Access, savedSecret!);
        Assert.Contains(Refresh, savedSecret!);
        AssertNoSentinels(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { result.Account, result.Snapshot })));
        AssertNoSentinelsInFiles();

        if (scenario == "failed-commit")
        {
            await using var connection = new SqliteConnection($"Data Source={Database}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER reject_acceptance BEFORE UPDATE ON state WHEN NEW.key='pending-credentials' BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END";
            await command.ExecuteNonQueryAsync();
            var error = await Assert.ThrowsAsync<SqliteException>(() =>
                store.WriteMonitoringStateAsync([result.Account], [result.Snapshot], historySnapshot: result.Snapshot));
            AssertNoSentinels(Encoding.UTF8.GetBytes(error.ToString()));
            var restarted = await new LocalStore(Database).LoadMonitoringStateAsync();
            Assert.Empty(restarted.Accounts);
            Assert.Equal(result.Account.CredentialId, Assert.Single(restarted.Retired).CredentialId);
            command.CommandText = "DROP TRIGGER reject_acceptance";
            await command.ExecuteNonQueryAsync();
        }
        else
        {
            await store.WriteMonitoringStateAsync([result.Account], [result.Snapshot], historySnapshot: result.Snapshot);
            Assert.Single(await store.HistoryAsync(result.Account.Id));
            // Persist removal and its retry state before deleting the protected credential.
            await store.WriteMonitoringStateAsync([], [], [result.Account]);
        }

        AssertNoSentinelsInFiles();
        await monitor.DisconnectAsync(result.Account);
        Assert.Null(await secrets.ReadAsync(result.Account.CredentialId!));
        AssertNoSentinelsInFiles();
    }

    void AssertNoSentinelsInFiles()
    {
        Assert.True(File.Exists(Database));
        foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var content = new MemoryStream();
            file.CopyTo(content);
            AssertNoSentinels(content.ToArray());
        }
    }

    static void AssertNoSentinels(byte[] bytes)
    {
        foreach (var sentinel in new[] { Access, Refresh })
        foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
            Assert.True(bytes.AsSpan().IndexOf(encoding.GetBytes(sentinel)) < 0,
                "Synthetic credential appeared outside the protected in-memory read.");
    }

    sealed class Handler(bool reject) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(Access, request.Headers.Authorization?.Parameter);
            AssertNoSentinels(Encoding.UTF8.GetBytes(request.RequestUri!.ToString()));
            var body = request.RequestUri.AbsolutePath == "/user"
                ? JsonSerializer.Serialize(new { id = 42, login = "synthetic", ignoredAccess = Access, ignoredRefresh = Refresh })
                : JsonSerializer.Serialize(new { usageItems = Array.Empty<object>(), ignoredAccess = Access, ignoredRefresh = Refresh });
            return Task.FromResult(new HttpResponseMessage(reject ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
            { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    public void Dispose()
    {
        TestDatabasePools.Clear(Database);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }
}
