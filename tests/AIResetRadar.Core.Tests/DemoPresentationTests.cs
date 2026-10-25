using AIResetRadar.App.Presentation;
using Xunit;
namespace AIResetRadar.Core.Tests;
public class DemoPresentationTests
{
    [Fact] public void DemoEditsStayInThePreviewState()
    {
        var state = new DashboardState(); state.LoadDemo(DateTimeOffset.UtcNow);
        var account = state.Accounts[0];
        Assert.True(state.TryEditDemoAccount(account with { Nickname = "Preview name" }));
        Assert.Equal("Preview name", state.Accounts[0].Nickname);
        state.ClearDemo(); Assert.False(state.TryEditDemoAccount(account)); Assert.Empty(state.Accounts);
    }
    [Fact] public void ReserveOnlyScenarioHasNavigableCreditDataAndNoQuotaRows()
    {
        var state = new DashboardState(); state.LoadDemo(DateTimeOffset.UtcNow); state.ApplyDemoScenario("reserve-only");
        Assert.True(state.IsDemo); Assert.Empty(state.Rows); Assert.NotNull(state.Snapshots[state.Accounts[0].Id].Reserve);
    }
    [Fact] public void TimelineCarriesTheExactQuotaWindowIdentity()
    {
        var state = new DashboardState(); var now = DateTimeOffset.UtcNow; state.LoadDemo(now);
        Assert.All(state.Events(now).Where(e => !e.IsExpiry), e => Assert.False(string.IsNullOrWhiteSpace(e.WindowId)));
    }
}
