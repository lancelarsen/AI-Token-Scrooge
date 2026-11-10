using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
using Xunit;
namespace AIResetRadar.Core.Tests;
public class GitHubQuotaRowTests
{
    [Theory]
    [InlineData(1250,null,"1,250 Credits Used","Limit Unavailable")]
    [InlineData(0,null,"0 Credits Used","Limit Unavailable")]
    [InlineData(null,null,"Usage Unavailable","Limit Unavailable")]
    [InlineData(0,0,"0 Credits Used","0 Limit")]
    [InlineData(1250,5000,"25% used","75% left")]
    public void ShowsKnownUsageWithoutInventingPercentage(int? used,int? limit,string expectedUsed,string expectedLeft)
    {
        var account=new ProviderAccount("a","GitHub Copilot","Work","42","personal");
        var window=new QuotaWindow("copilot/monthly","AI Credits",UsageUnit.Credits,used,limit,null);
        var row=new QuotaRow(account,new("a","42","personal",DateTimeOffset.UtcNow,[window],null),window);
        Assert.Equal(expectedUsed,row.UsedText); Assert.Equal(expectedLeft,row.LeftText);
    }
}
