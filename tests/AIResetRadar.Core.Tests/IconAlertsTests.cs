using System.Text.Json;
using Xunit;
namespace AIResetRadar.Core.Tests;
public class IconAlertsTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    static readonly ProviderAccount Account = new("a", "OpenAI", "Work", "s", "s");
    static Dictionary<string, QuotaSnapshot> Snapshots(decimal used = 75) => new() { ["a"] = new("a", "s", "s", Now, [new("w", "Weekly", UsageUnit.Percent, used, 100, null)], null) };
    [Fact] public void BothChannelsDefaultOnAndRoundTripIndependently()
    {
        var defaults = JsonSerializer.Deserialize<RadarSettings>("{}")!;
        Assert.True(defaults.Notifications); Assert.True(defaults.AlertOnIcon);
        var saved = JsonSerializer.Deserialize<RadarSettings>(JsonSerializer.Serialize(defaults with { AlertOnIcon = false }))!;
        Assert.True(saved.Notifications); Assert.False(saved.AlertOnIcon);
    }
    [Fact] public void IconWorksWithoutPopupsAndClearsWhenDisabled()
    {
        var settings = new RadarSettings(Notifications: false);
        Assert.Equal(ReminderSeverity.Warning, IconAlerts.Evaluate([Account], Snapshots(), settings, Now)!.Severity);
        Assert.Null(IconAlerts.Evaluate([Account], Snapshots(), settings with { AlertOnIcon = false }, Now));
        Assert.Null(IconAlerts.Evaluate([Account], Snapshots(0), settings, Now));
    }
    [Fact] public void MutedAndUnavailableAccountsDoNotRaiseUsageAlerts()
    {
        Assert.Null(IconAlerts.Evaluate([Account], Snapshots(), new RadarSettings().WithCapacityAlerts("a", "w", false), Now));
        Assert.Null(IconAlerts.Evaluate([Account with { Enabled = false }], Snapshots(), new(), Now));
        var stale = Snapshots(); stale["a"] = stale["a"] with { Health = SourceHealth.Stale };
        Assert.Null(IconAlerts.Evaluate([Account], stale, new(), Now));
    }
    [Fact] public void CriticalReminderWinsOverInfoUsage()
    {
        var data = Snapshots(55); data["a"] = data["a"] with { Reserve = new(1, [new("c", "available", Now.AddMinutes(30))]) };
        Assert.Equal(ReminderSeverity.Critical, IconAlerts.Evaluate([Account], data, new(), Now)!.Severity);
    }
}
