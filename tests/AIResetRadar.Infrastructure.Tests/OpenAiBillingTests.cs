using System.Text.Json;
using AIResetRadar.Core;
using AIResetRadar.Infrastructure;
using AIResetRadar.App.Presentation;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public sealed class OpenAiBillingTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
    static readonly OpenAiBillingIdentity Identity = new("user-fixture", new HashSet<string> { "org-fixture" });
    static readonly ProviderAccount Account = new("account-fixture", "OpenAI API", "API", "user-fixture", "org-fixture",
        Unit: UsageUnit.Usd, CredentialId: "11111111111111111111111111111111", ProfileLocation: CredentialProfileLocation.LocalSecurityDirectory);

    static JsonElement Payload(decimal granted = 75, decimal used = 12.345678m, decimal? available = null, object[]? grants = null) =>
        JsonSerializer.SerializeToElement(new { total_granted = granted, total_used = used, total_available = available ?? granted - used,
            grants = new { data = grants ?? new object[] { Grant("grant-fixture", 75, 12.345678m, 1800003600) } } });

    static object Grant(string id, decimal amount, decimal used, long? expiry, bool scheduled = false, bool held = false, long effective = 1799990000) =>
        new { id, grant_amount = amount, used_amount = used, expires_at = expiry, effective_at = effective,
            is_scheduled = scheduled, is_held_for_prepaid_transition = held };

    [Fact]
    public void RespectsProviderRetryDatesAndDurations()
    {
        Assert.Equal(Now.AddHours(2), OpenAiBilling.RetryAt("7200", Now));
        Assert.Equal(Now.AddHours(3), OpenAiBilling.RetryAt(Now.AddHours(3).ToString("R"), Now));
        Assert.Equal(Now.AddMinutes(15), OpenAiBilling.RetryAt("unreadable", Now));
    }

    [Fact]
    public void ReadsExactDollarsAndExpirationWithoutInventingMonthlyAllowanceOrReset()
    {
        var reading = OpenAiBilling.Read(Identity, Account.Scope, Payload(), Now);
        var snapshot = OpenAiBilling.Snapshot(Account, reading, Now);
        var window = Assert.Single(snapshot.Windows);
        Assert.Equal(62.654322m, window.Remaining);
        Assert.Equal(UsageUnit.Usd, window.Unit);
        Assert.Null(window.ResetsAt);
        Assert.Null(snapshot.Reserve);
        Assert.Null(snapshot.ProviderDataAt);
        Assert.Equal(Now.AddHours(1), Assert.Single(snapshot.ApiCreditGrants!).ExpiresAt);
        Assert.Equal("$12.35 Used", new QuotaRow(Account, snapshot, window).UsedText);
        Assert.Equal("$62.65 Left", new QuotaRow(Account, snapshot, window).LeftText);
    }

    [Fact]
    public void KeepsZeroAndOverageAndUnknownExpiry()
    {
        var empty = OpenAiBilling.Read(Identity, Account.Scope, Payload(0, 0, grants: []), Now);
        Assert.Equal(0, empty.Available);
        Assert.Equal(CapacityState.Exhausted, OpenAiBilling.Snapshot(Account, empty, Now).Windows[0].Capacity());
        var overage = OpenAiBilling.Read(Identity, Account.Scope, Payload(75, 78, grants: []), Now);
        Assert.Equal(-3, OpenAiBilling.Snapshot(Account, overage, Now).Windows[0].Remaining);
        var unknown = OpenAiBilling.Read(Identity, Account.Scope, Payload(grants: [Grant("g", 75, 0, null)]), Now);
        Assert.Null(Assert.Single(unknown.Grants).ExpiresAt);
    }

    [Fact]
    public void RejectsWrongOrganizationAndChangedIdentity()
    {
        Assert.Throws<ProviderAccessException>(() => OpenAiBilling.Read(Identity, "org-other", Payload(), Now));
        var reading = OpenAiBilling.Read(Identity, Account.Scope, Payload(), Now);
        Assert.Throws<ProviderAccessException>(() => OpenAiBilling.Snapshot(Account with { Subject = "user-other" }, reading, Now));
        Assert.Throws<ProviderAccessException>(() => OpenAiBilling.Snapshot(Account with { Scope = "org-other" }, reading, Now));
    }

    [Fact]
    public void IgnoresScheduledHeldExpiredAndDepletedGrants()
    {
        var reading = OpenAiBilling.Read(Identity, Account.Scope, Payload(grants: [
            Grant("scheduled", 20, 0, 1800003600, scheduled: true),
            Grant("held", 20, 0, 1800003600, held: true),
            Grant("expired", 20, 0, 1799999999),
            Grant("depleted", 20, 20, 1800003600),
            Grant("future", 20, 0, 1800007200, effective: 1800003600),
            Grant("available", 75, 12.345678m, 1800003600)]), Now);
        Assert.Equal("available", Assert.Single(reading.Grants).Id);
    }

    [Fact]
    public void RejectsMalformedTotalsMissingFieldsAndDuplicateGrants()
    {
        Assert.Throws<ProviderAccessException>(() => OpenAiBilling.Read(Identity, Account.Scope, Payload(available: 99), Now));
        Assert.Throws<ProviderAccessException>(() => OpenAiBilling.Read(Identity, Account.Scope, Payload(-1, 0), Now));
        Assert.Throws<ProviderAccessException>(() => OpenAiBilling.Read(Identity, Account.Scope, JsonSerializer.SerializeToElement(new { }), Now));
        Assert.Throws<ProviderAccessException>(() => OpenAiBilling.Read(Identity, Account.Scope,
            Payload(grants: [Grant("same", 1, 0, null), Grant("same", 1, 0, null)]), Now));
    }

    [Fact]
    public void IdentityParserRetainsOnlyStableIdentityAndMembership()
    {
        var identity = OpenAiBilling.Identity(JsonSerializer.SerializeToElement(new { user = new {
            id = "user-fixture", session = new { sensitive = "SYNTHETIC-SECRET" }, email = "private@example.invalid",
            orgs = new { data = new[] { new { id = "org-fixture", name = "Private Organization" } } } } }));
        var stored = JsonSerializer.Serialize(identity);
        Assert.DoesNotContain("SYNTHETIC", stored);
        Assert.DoesNotContain("private@", stored);
        Assert.DoesNotContain("Private Organization", stored);
        Assert.Contains("org-fixture", stored);
    }

    [Fact]
    public void UsesExpirationEventsAndRefreshesAtTheBoundary()
    {
        var snapshot = OpenAiBilling.Snapshot(Account, OpenAiBilling.Read(Identity, Account.Scope, Payload(), Now), Now);
        var row = new QuotaRow(Account, snapshot, snapshot.Windows[0]);
        Assert.Equal((Now.AddHours(1), true), ResetCountdown.Select(row, Now));
        var dashboard = new DashboardState(); dashboard.Accounts.Add(Account); dashboard.Snapshots[Account.Id] = snapshot;
        var item = Assert.Single(dashboard.Events(Now));
        Assert.True(item.IsExpiry); Assert.Equal("API Credits Expire", item.Title);
        Assert.Equal("openai-api/credits", item.WindowId);
        Assert.Equal(Now.AddHours(1), RefreshSchedule.AfterSuccess(snapshot, Account, Now.AddMinutes(59)));
        var history = UsageHistory.Build(Account, [snapshot], Now.AddSeconds(-1), Now.AddHours(2));
        Assert.Single(history.Points);
        Assert.Equal(HistoryEventKind.ReportedExpiration, Assert.Single(history.Events).Kind);
    }

    [Fact]
    public void ConsumedGrantDoesNotBecomeAFalseHistoricalExpiration()
    {
        var snapshot = OpenAiBilling.Snapshot(Account, OpenAiBilling.Read(Identity, Account.Scope, Payload(), Now), Now);
        var consumed = snapshot with { ObservedAt = Now.AddMinutes(10), ApiCreditGrants = [] };
        Assert.Empty(UsageHistory.Build(Account, [snapshot, consumed], Now, Now.AddHours(2)).Events);
    }

    [Fact]
    public async Task ClientJournalsBeforeSignInAndRejectsChangedRefreshIdentity()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), "scrooge-api-client-" + Guid.NewGuid().ToString("N"));
        var journal = new Journal();
        var browser = new Browser(journal);
        var client = new OpenAiBillingClient(directory, directory, new Secrets(), browser, journal, TimeProvider.System);
        try
        {
            var (account, _) = await client.ConnectAsync(Account.Id, Account.CredentialId!, "API", default);
            Assert.True(browser.Interactive);
            Assert.Equal(10, account.PollingMinutes);
            await client.RefreshAsync(account, default);
            Assert.False(browser.Interactive);
            Assert.Equal(account.Subject, browser.ExpectedSubject);
            Assert.Equal(account.Scope, browser.ExpectedOrganization);
            browser.WrongIdentity = true;
            await Assert.ThrowsAsync<ProviderAccessException>(() => client.RefreshAsync(account, default));
            browser.WrongIdentity = false;
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RefreshAsync(account, cancelled.Token));
            await client.DisconnectAsync(account, default);
            Assert.True(journal.Completed);
            Assert.False(Directory.Exists(browser.Profile));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    sealed class Journal : ICredentialLifecycleJournal
    {
        public bool Staged, Completed;
        public Task StageCredentialAsync(string provider, string credentialId, CancellationToken ct = default,
            CredentialProfileLocation profileLocation = CredentialProfileLocation.LegacyDataDirectory)
        { Assert.Equal(OpenAiBilling.Provider, provider); Staged = true; return Task.CompletedTask; }
        public Task CompleteCredentialCleanupAsync(string credentialId, CancellationToken ct = default)
        { Completed = true; return Task.CompletedTask; }
    }
    sealed class Secrets : ISecretStore
    {
        public Task SaveAsync(string id, string value) => throw new InvalidOperationException("Browser credentials must not be copied.");
        public Task<string?> ReadAsync(string id) => throw new InvalidOperationException("Browser credentials must not be copied.");
        public void Remove(string id) => throw new InvalidOperationException("Use the owned profile cleanup.");
    }
    sealed class Browser(Journal journal) : IOpenAiBillingBrowser
    {
        public bool Interactive, WrongIdentity;
        public string? ExpectedSubject, ExpectedOrganization, Profile;
        public Task<OpenAiBillingReading> ReadAsync(string profile, bool interactive, string? expectedSubject, string? expectedOrganization, CancellationToken ct)
        {
            Assert.True(journal.Staged); ct.ThrowIfCancellationRequested();
            Profile = profile; Interactive = interactive; ExpectedSubject = expectedSubject; ExpectedOrganization = expectedOrganization;
            var reading = OpenAiBilling.Read(Identity, Account.Scope, Payload(), Now);
            return Task.FromResult(WrongIdentity ? reading with { Subject = "wrong-user" } : reading);
        }
    }

    [Fact]
    public async Task PersistsGrantMetadataAndNeverDowngradesTheCredentialVersionGate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "scrooge-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "radar.db");
        try
        {
            var store = new LocalStore(path);
            var snapshot = OpenAiBilling.Snapshot(Account, OpenAiBilling.Read(Identity, Account.Scope, Payload(), Now), DateTimeOffset.UtcNow);
            await store.StageCredentialAsync(Account.Provider, Account.CredentialId!, profileLocation: Account.ProfileLocation);
            await store.WriteMonitoringStateAsync([Account], [snapshot], historySnapshot: snapshot);
            var history = await store.HistoryAsync(Account.Id);
            Assert.Equal(snapshot.ApiCreditGrants, Assert.Single(history).ApiCreditGrants);
            await store.StageCredentialAsync("OpenAI", "22222222222222222222222222222222", profileLocation: Account.ProfileLocation);
            await store.WriteMonitoringStateAsync([Account with { Provider = "OpenAI" }], []);
            await using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version";
            Assert.Equal(5L, await command.ExecuteScalarAsync());
        }
        finally
        {
            SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
            Directory.Delete(directory, true);
        }
    }
}
