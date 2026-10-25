using System.Text.Json;
using AIResetRadar.Core;
using Xunit;
namespace AIResetRadar.Infrastructure.Tests;

public class ProviderMappingTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T14:46:00Z");
    static readonly ProviderAccount Account = new("a", "OpenAI", "Personal", "sample@example.invalid", "workspace-a");
    static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    [Fact] public void CodexPrefersMultiBucketAndPreservesCountOnlyReserve()
    {
        var result = ProviderMapping.Codex(Account, Json("""
        {"accountId":"workspace-a","rateLimits":{"primary":{"usedPercent":99}},
         "rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":38,"windowDurationMins":300,"resetsAt":1792938000},
         "secondary":{"usedPercent":74,"windowDurationMins":10080}}},"rateLimitResetCredits":{"availableCount":2,"credits":null}}
        """), Now);
        Assert.Equal(2, result.Windows.Count);
        Assert.Equal(62, result.Windows[0].RemainingPercent);
        Assert.Equal(26, result.Windows[1].RemainingPercent);
        Assert.Equal(2, result.Reserve!.AvailableCount);
        Assert.Null(result.Reserve.Credits);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792938000), result.Windows[0].ResetsAt);
    }

    [Fact] public void LegacyCodexAndAbsentReserveRemainSupported()
    {
        var result = ProviderMapping.Codex(Account, Json("""{"rateLimits":{"primary":{"usedPercent":10}}}"""), Now);
        Assert.Single(result.Windows);
        Assert.Null(result.Windows[0].ResetsAt);
        Assert.Null(result.Reserve);
    }

    [Fact] public void DifferentWorkspaceIsRejectedBeforeAttachingData()
    {
        Assert.Throws<InvalidDataException>(() => ProviderMapping.Codex(Account, Json("""{"accountId":"workspace-b"}"""), Now));
    }

    [Fact] public void CopilotMissingAllowanceDoesNotInventAQuota()
    {
        var account = Account with { Provider = "GitHub Copilot", Scope = "personal", Subject = "42" };
        var result = ProviderMapping.GitHub(account, Json("""{"usageItems":[{"unitType":"ai-credits","grossQuantity":8200}]}"""), Now);
        Assert.Equal(8200, result.Windows[0].Used);
        Assert.Null(result.Windows[0].Remaining);
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T00:00:00Z"), result.Windows[0].ResetsAt);
    }

    [Fact] public void CopilotSumsModelRowsInSelectedUnitOnly()
    {
        var account = Account with { Provider = "GitHub Copilot", Allowance = 10000, Unit = UsageUnit.Credits };
        var result = ProviderMapping.GitHub(account, Json("""
        {"usageItems":[{"unitType":"ai-credits","grossQuantity":8000},
        {"unitType":"credits","grossQuantity":200},{"unitType":"requests","grossQuantity":900}]}
        """), Now);
        Assert.Equal(1800, result.Windows[0].Remaining);
        Assert.Equal(AllowanceProvenance.Manual, result.Windows[0].AllowanceSource);
    }

    [Fact] public void CodexNullExpiryAndDuplicateDetailsDoNotInventAReserve()
    {
        var result = ProviderMapping.Codex(Account, Json("""
        {"rateLimitsByLimitId":{"future_bucket":null},"rateLimitResetCredits":{"availableCount":3,"credits":[
        {"id":"x","status":"available","expiresAt":null},{"id":"x","status":"available","expiresAt":null}]}}
        """), Now);
        Assert.Empty(result.Windows);
        Assert.Equal(3, result.Reserve!.AvailableCount);
        Assert.Single(result.Reserve.Credits!);
        Assert.True(result.Reserve.HasPartialDetails);
        Assert.Null(result.Reserve.EarliestExpiry(Now));
    }

    [Fact] public void CopilotDecemberPeriodResetsInJanuaryUtc()
    {
        var result = ProviderMapping.GitHub(Account, Json("""{"usageItems":[]}"""), DateTimeOffset.Parse("2026-12-31T23:59:00Z"));
        Assert.Equal(DateTimeOffset.Parse("2027-01-01T00:00:00Z"), result.Windows[0].ResetsAt);
    }

    [Fact] public void CopilotUnrecognizedUnitsCannotMeanZeroUsage()
    {
        var result = ProviderMapping.GitHub(Account with { Allowance = 100 }, Json("""{"usageItems":[{"unitType":"future-units","grossQuantity":80}]}"""), Now);
        Assert.Null(result.Windows[0].Used);
        Assert.Null(result.Windows[0].RemainingPercent);
    }

    [Fact] public void CopilotRejectsIncompleteSelectedQuantities()
    {
        Assert.Throws<InvalidDataException>(() => ProviderMapping.GitHub(Account,
            Json("""{"usageItems":[{"unitType":"credits","grossQuantity":-1}]}"""), Now));
    }

    [Fact] public void MalformedCodexTimestampsAndCountsStayUnknown()
    {
        var result = ProviderMapping.Codex(Account, Json("""
            {"rateLimits":{"primary":{"usedPercent":50,"windowDurationMins":9999999999999999999,"resetsAt":9999999999999999999}},
             "rateLimitResetCredits":{"availableCount":1.5,"credits":null}}
            """), Now);
        Assert.Null(result.Windows[0].ResetsAt); Assert.Null(result.Windows[0].DurationMinutes);
        Assert.Null(result.Reserve!.AvailableCount);
    }
}
