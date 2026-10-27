using System.Text.Json;
using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class AccountOrderingTests
{
    static DashboardState Demo()
    {
        var state = new DashboardState(); state.LoadDemo(DateTimeOffset.Parse("2026-10-25T12:00:00Z")); return state;
    }

    [Theory]
    [InlineData(AccountSortMode.MostRemaining, "demo-work,demo-personal,demo-github")]
    [InlineData(AccountSortMode.LeastRemaining, "demo-github,demo-personal,demo-work")]
    [InlineData(AccountSortMode.NextReset, "demo-personal,demo-work,demo-github")]
    public void SortsWholeAccountsUsingTheirTightestWindow(AccountSortMode mode, string expected)
    {
        var state = Demo(); state.Settings = new(AccountSort: mode);
        Assert.Equal(expected.Split(','), state.OrderedAccounts.Select(a => a.Id));
        var rows = state.Rows.ToArray();
        Assert.Equal(new[] { "codex/primary", "codex/secondary" }, rows.Where(r => r.Account.Id == "demo-personal").Select(r => r.Window.Id));
        Assert.Equal(3, rows.Select(r => r.Account.Id).Distinct().Count());
        Assert.Equal(expected.Split(','), rows.GroupBy(r => r.Account.Id).Select(g => g.Key));
    }

    [Fact]
    public void MissingUsageStaysLastInBothCapacitySorts()
    {
        var state = Demo(); state.ApplyDemoScenario("unknown");
        var known = state.Snapshots["demo-work"];
        state.Snapshots[known.AccountId] = known with { Windows = known.Windows.Select(w => w with { Used = 20, Limit = 100 }).ToArray() };
        foreach (var mode in new[] { AccountSortMode.MostRemaining, AccountSortMode.LeastRemaining })
        {
            state.Settings = new(AccountSort: mode);
            Assert.Equal("demo-work", state.OrderedAccounts.First().Id);
        }
    }

    [Fact]
    public void CustomOrderSurvivesSerializationAndOtherSortSelections()
    {
        var state = Demo();
        var order = AccountOrdering.Normalize(["removed", "demo-work", "demo-work", "demo-personal"], state.Accounts.Select(a => a.Id));
        Assert.Equal(new[] { "demo-work", "demo-personal", "demo-github" }, order);
        order = AccountOrdering.Move(order, "demo-github", 0);
        var settings = new RadarSettings(AccountSort: AccountSortMode.CustomOrder, AccountOrder: order);
        state.Settings = JsonSerializer.Deserialize<RadarSettings>(JsonSerializer.Serialize(settings))!;
        Assert.Equal(new[] { "demo-github", "demo-work", "demo-personal" }, state.OrderedAccounts.Select(a => a.Id));
        state.Settings = state.Settings with { AccountSort = AccountSortMode.NextReset };
        state.Settings = state.Settings with { AccountSort = AccountSortMode.CustomOrder };
        Assert.Equal(order, state.OrderedAccounts.Select(a => a.Id));
        Assert.Equal(order, AccountOrdering.Move(order, "missing", 0));
    }
}
