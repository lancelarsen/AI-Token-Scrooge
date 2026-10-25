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
}
