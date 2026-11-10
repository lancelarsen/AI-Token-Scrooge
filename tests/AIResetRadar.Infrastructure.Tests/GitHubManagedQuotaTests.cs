using System.Net;
using System.Text;
using AIResetRadar.Core;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;
public class GitHubManagedQuotaTests
{
    sealed class Handler(string quota, string plan = "business") : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            var body = request.RequestUri.AbsolutePath switch {
                "/user" => """{"id":42,"login":"sample"}""",
                "/copilot_internal/user" => $$$"""{"copilot_plan":"{{{plan}}}","token_based_billing":true,"quota_snapshots":{"premium_interactions":{{{quota}}}}}""",
                _ => throw new Exception("Managed quotas must not read personal billing")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8,"application/json") });
        }
    }
    [Theory]
    [InlineData("business")]
    [InlineData("enterprise")]
    public async Task PersonalBudgetAndUsageComeFromSameVerifiedQuota(string plan)
    {
        var handler = new Handler("""{"entitlement":"50000","credits_used":"1250","quota_remaining":48750,"unlimited":false,"has_quota":false}""",plan);
        using var http = new HttpClient(handler);
        var result = await new GitHubUsageClient(http, TimeProvider.System).ReadAsync(new("a","GitHub Copilot","Work","42","personal",Allowance:100), "fixture-token",default);
        var window = Assert.Single(result.Windows);
        Assert.Equal(50000m,window.Limit); Assert.Equal(1250m,window.Used);
        Assert.Equal(AllowanceProvenance.Provider,window.AllowanceSource);
        Assert.Equal(plan,result.PlanType); Assert.Null(result.UsageAllowed);
        Assert.Equal(new[]{"/user","/copilot_internal/user"},handler.Paths);
    }
    [Theory]
    [InlineData("""{"unlimited":true,"entitlement":50000,"credits_used":1250,"has_quota":true}""",1250,null,false)]
    [InlineData("""{"unlimited":true,"credits_used":1250,"has_quota":false}""",1250,null,true)]
    [InlineData("""{"unlimited":true,"quota_remaining":50000}""",null,null,false)]
    [InlineData("""{"unlimited":false,"entitlement":50000,"quota_remaining":48750}""",1250,50000,false)]
    [InlineData("""{"unlimited":false,"entitlement":0,"credits_used":0}""",0,0,false)]
    [InlineData("""{"unlimited":false,"entitlement":100,"credits_used":125}""",125,100,false)]
    [InlineData("""{"unlimited":false,"entitlement":100,"quota_remaining":120}""",null,100,false)]
    [InlineData("""{"unlimited":false,"entitlement":"bad","credits_used":12}""",12,null,false)]
    [InlineData("""{"unlimited":false,"entitlement":100,"credits_used":-1}""",null,100,false)]
    public async Task PreservesUnknownZeroOverageAndPooledSemantics(string quota, int? used, int? limit, bool blocked)
    {
        using var http=new HttpClient(new Handler(quota));
        var result=await new GitHubUsageClient(http,TimeProvider.System).ReadAsync(new("a","GitHub Copilot","Work","42","personal",Allowance:999),"fixture-token",default);
        var window=Assert.Single(result.Windows);
        Assert.Equal((decimal?)used,window.Used); Assert.Equal((decimal?)limit,window.Limit);
        Assert.Equal(blocked ? false : (bool?)null,result.UsageAllowed);
        if(limit is null) Assert.Equal(AllowanceProvenance.Unknown,window.AllowanceSource);
    }
    [Theory]
    [InlineData("organization:example",UsageUnit.Credits)]
    [InlineData("personal",UsageUnit.Requests)]
    public void DoesNotMixScopesOrUnits(string scope,UsageUnit unit)
    {
        using var data=System.Text.Json.JsonDocument.Parse("""{"copilot_plan":"business","token_based_billing":true,"quota_snapshots":{"premium_interactions":{"entitlement":50000,"credits_used":1250,"unlimited":false}}}""");
        Assert.Null(ProviderMapping.GitHubManagedQuota(new("a","GitHub Copilot","Work","42",scope,Unit:unit),data.RootElement,DateTimeOffset.UtcNow));
    }
}

