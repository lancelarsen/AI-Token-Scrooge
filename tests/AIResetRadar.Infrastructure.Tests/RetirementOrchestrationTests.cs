using AIResetRadar.Core;
using AIResetRadar.Infrastructure;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests
{
    public sealed class RetirementOrchestrationTests
    {
        [Theory]
        [InlineData(true, true, 0, 1, 1)]
        [InlineData(false, true, 1, 1, 0)]
        [InlineData(false, false, 1, 0, 0)]
        public async Task CleanupUsesLiveInventoryAndNeverDemoAccounts(bool demo, bool replacementExists, int disconnects, int historyRows, int pending)
        {
            var folder = Path.Combine(Path.GetTempPath(), "AIResetRadar-tests", Guid.NewGuid().ToString("N"));
            try
            {
                var store = new LocalStore(Path.Combine(folder, "radar.db"));
                var account = new ProviderAccount("saved", "GitHub Copilot", "Saved", "qa-user", "personal", CredentialId: "11111111111111111111111111111111");
                var retired = account with { CredentialId = "22222222222222222222222222222222" };
                var snapshot = new QuotaSnapshot(account.Id, account.Subject, account.Scope, DateTimeOffset.UtcNow, [], null);
                await store.WriteMonitoringStateAsync(replacementExists ? [account] : [], [snapshot], [retired]);
                await store.RecordSnapshotAsync(snapshot);
                var monitor = new AIResetRadar.App.Services.TestCredentialMonitor();
                var page = new AIResetRadar.App.MainPage(store, monitor, replacementExists ? account : null, retired, demo);
                await page.RunCleanupAsync();
                Assert.Equal(disconnects, monitor.Disconnected.Count);
                Assert.Equal(historyRows, (await store.HistoryAsync(account.Id)).Count);
                Assert.Equal(pending, (await store.ReadAsync<ProviderAccount[]>("retired"))!.Length);
                Assert.Equal(replacementExists ? 1 : 0, (await store.ReadAsync<ProviderAccount[]>("accounts"))!.Length);
            }
            finally
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}

// Compile the production account orchestration unchanged, replacing only its UI and
// credential transport boundaries. SQLite/history and DashboardState are real.
namespace AIResetRadar.App
{
    public sealed partial class MainPage
    {
        readonly Presentation.DashboardState state = new();
        readonly Dictionary<string, DateTimeOffset> nextRefresh = [], throttledUntil = [];
        readonly Dictionary<string, int> failures = [];
        readonly TestLabel freshness = new();
        sealed class TestLabel { public string? Text { get; set; } }
        public MainPage(LocalStore store, Services.TestCredentialMonitor monitor, ProviderAccount? live, ProviderAccount retired, bool demo)
        {
            Services.AppServices.Current = new() { Store = store, Monitor = monitor };
            if (live is not null) state.Accounts.Add(live);
            retiredCredentials.Add(retired);
            if (demo) state.LoadDemo(DateTimeOffset.UtcNow);
        }
        public Task RunCleanupAsync() => CleanupRetiredAsync();
        void Render() { }
        void UpdateTray() { }
        Task EvaluateAlertsAsync(ProviderAccount account, QuotaSnapshot? previous, QuotaSnapshot current) => Task.CompletedTask;
    }
}

namespace AIResetRadar.App.Services
{
    public sealed class AppServices
    {
        public static AppServices Current { get; set; } = null!;
        public LocalStore Store { get; init; } = null!;
        public TestCredentialMonitor Monitor { get; init; } = null!;
    }
    public sealed class TestCredentialMonitor
    {
        public List<ProviderAccount> Disconnected { get; } = [];
        public Task DisconnectAsync(ProviderAccount account, CancellationToken cancellationToken)
        { Disconnected.Add(account); return Task.CompletedTask; }
    }
}
