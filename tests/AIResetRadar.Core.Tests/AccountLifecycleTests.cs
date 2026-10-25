using Xunit;
namespace AIResetRadar.Core.Tests;
public class AccountLifecycleTests
{
    static readonly ProviderAccount Original = new("a", "OpenAI", "Work", "sample", "workspace", Enabled: false);
    [Fact] public void ReconnectPreservesPauseAndLogicalIdentity()
    {
        var result = ReconnectPolicy.Accept(Original, Original with { Enabled = true, CredentialId = "new-profile" });
        Assert.False(result.Enabled); Assert.Equal(Original.Id, result.Id); Assert.Equal("new-profile", result.CredentialId);
    }
    [Fact] public void DifferentIdentityCannotReplaceTheOriginalConnection() =>
        Assert.Throws<InvalidDataException>(() => ReconnectPolicy.Accept(Original, Original with { Subject = "other" }));
    [Fact] public void ResultsFromBeforeReconnectOrPauseCannotAttach()
    {
        var account = Original with { Enabled = true }; var lease = new AccountRefreshLease(account);
        Assert.True(lease.IsCurrent(account)); Assert.False(lease.IsCurrent(null));
        Assert.False(lease.IsCurrent(account with { CredentialId = "new-profile" }));
        var resumed = (account with { Enabled = false }) with { Enabled = true };
        Assert.Equal(account, resumed); Assert.False(lease.IsCurrent(resumed));
    }

    [Fact] public void GitHubBillingScopesAreCaseInsensitiveOnReconnectAndRestore()
    {
        var original = Original with { Provider = "GitHub Copilot", Scope = "organization:Example" };
        var result = ReconnectPolicy.Accept(original, original with { Scope = "organization:example" });
        Assert.False(result.Enabled);
        Assert.True(new QuotaSnapshot(original.Id, original.Subject, "organization:EXAMPLE", DateTimeOffset.UtcNow, [], null).BelongsTo(original));
    }
    [Fact] public void CachedSnapshotCannotAttachToAnotherSubjectOrWorkspace()
    {
        var snapshot = new QuotaSnapshot(Original.Id, Original.Subject, Original.Scope, DateTimeOffset.UtcNow, [], null);
        Assert.True(snapshot.BelongsTo(Original));
        Assert.False((snapshot with { Subject = "other" }).BelongsTo(Original));
        Assert.False((snapshot with { Scope = "other" }).BelongsTo(Original));
    }
}
