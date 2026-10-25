using Xunit;
namespace AIResetRadar.Core.Tests;
public class ProviderLinkTests
{
    [Fact] public void ProviderLinksCarryNoAccountSubjectOrCredential()
    {
        var account = new ProviderAccount("id", "OpenAI", "Work", "private@example.invalid", "workspace", CredentialId: "secret-reference");
        var uri = ProviderLinks.Usage(account)!;
        Assert.Equal("https", uri.Scheme); Assert.Equal("chatgpt.com", uri.Host);
        Assert.DoesNotContain(account.Subject, uri.AbsoluteUri); Assert.DoesNotContain(account.CredentialId!, uri.AbsoluteUri); Assert.Empty(uri.Query);
    }
    [Fact] public void OrganizationBillingUsesAnEscapedPathOnTheGitHubOrigin()
    {
        var uri = ProviderLinks.Usage(new("id", "GitHub Copilot", "Work", "42", "organization:example/../../other.invalid"))!;
        Assert.Equal("github.com", uri.Host); Assert.Contains("example%2F..%2F..%2Fother.invalid", uri.AbsoluteUri);
    }
    [Fact] public void UnsupportedProviderDoesNotOpenAnInventedDashboard() => Assert.Null(ProviderLinks.Usage(new("id", "Future service", "Work", "subject", "scope")));
}
