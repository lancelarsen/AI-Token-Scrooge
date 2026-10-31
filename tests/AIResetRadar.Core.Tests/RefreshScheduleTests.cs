using Xunit;
namespace AIResetRadar.Core.Tests;
public class RefreshScheduleTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T14:46:00Z");
    static ProviderAccount Account(string provider = "OpenAI", int minutes = 2) => new("a", provider, "Personal", "subject", "scope", PollingMinutes: minutes);
    static QuotaSnapshot Snapshot(DateTimeOffset reset, ResetReserve? reserve = null) => new("a", "subject", "scope", Now,
        [new("weekly", "Weekly", UsageUnit.Percent, 80, 100, reset)], reserve);

    [Fact] public void KnownResetInterruptsOrdinaryPollingInterval() =>
        Assert.Equal(Now.AddSeconds(20), RefreshSchedule.AfterSuccess(Snapshot(Now.AddSeconds(20)), Account(minutes: 60), Now));
    [Fact] public void EarnedResetExpiryInterruptsOrdinaryPollingInterval() =>
        Assert.Equal(Now.AddSeconds(10), RefreshSchedule.AfterSuccess(Snapshot(Now.AddDays(2),
            new(1, [new("c", "available", Now.AddSeconds(10))])), Account(), Now));
    [Fact] public void AlreadyPassedBoundaryCannotCreateAnImmediateRetryLoop() =>
        Assert.Equal(Now.AddSeconds(120), RefreshSchedule.AfterSuccess(Snapshot(Now.AddSeconds(-1)), Account(), Now));
    [Theory]
    [InlineData("OpenAI")]
    [InlineData("GitHub Copilot")]
    [InlineData("Claude")]
    public void EveryProviderDefaultsToTwoMinutes(string provider) =>
        Assert.Equal(Now.AddMinutes(2), RefreshSchedule.AfterSuccess(Snapshot(Now.AddDays(2)), Account(provider), Now));

    [Theory]
    [InlineData(1, 1)]
    [InlineData(15, 15)]
    [InlineData(30, 30)]
    [InlineData(1440, 30)]
    [InlineData(0, 1)]
    [InlineData(int.MaxValue, 30)]
    public void CustomIntervalIsBoundedAndUsed(int configured, int expected) =>
        Assert.Equal(Now.AddMinutes(expected), RefreshSchedule.AfterSuccess(Snapshot(Now.AddDays(2)), Account(minutes: configured), Now));

    [Fact] public void ExistingAccountsDefaultToTwoMinutesAndCustomIntervalsSurviveSerialization()
    {
        var json = """{"Id":"a","Provider":"Claude","Nickname":"Personal","Subject":"subject","Scope":"scope"}""";
        var restored = System.Text.Json.JsonSerializer.Deserialize<ProviderAccount>(json)!;
        Assert.Equal(2, restored.EffectivePollingMinutes);
        var saved = System.Text.Json.JsonSerializer.Serialize(restored with { PollingMinutes = 17 });
        Assert.Equal(17, System.Text.Json.JsonSerializer.Deserialize<ProviderAccount>(saved)!.EffectivePollingMinutes);
    }

    [Fact] public void ReconnectionPreservesCustomPolling()
    {
        var account = Account(minutes: 17);
        Assert.Equal(17, ReconnectPolicy.Accept(account, account with { CredentialId = "new", PollingMinutes = 2 }).EffectivePollingMinutes);
    }
}
