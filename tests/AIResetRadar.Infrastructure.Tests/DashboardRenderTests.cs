using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public class DashboardRenderTests
{
    [Fact]
    public void MidnightRefreshesTodayLabelsWithoutAProviderObservation()
    {
        var before = new DateTimeOffset(new DateTime(2026, 10, 25, 23, 59, 59, DateTimeKind.Local));
        var state = new DashboardState();
        state.LoadDemo(before);
        var after = before.AddSeconds(1);
        Assert.Equal(state.Events(before).ToArray(), state.Events(after).ToArray());
        Assert.NotEqual(state.RenderSignature(before), state.RenderSignature(after));
    }

    [Fact]
    public void CountdownTicksPreserveControlsButResetBoundariesInvalidateThem()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var state = new DashboardState();
        state.LoadDemo(now);
        Assert.Equal(state.RenderSignature(now), state.RenderSignature(now.AddSeconds(30)));
        var reset = state.Rows.Min(r => r.Window.ResetsAt)!.Value;
        Assert.NotEqual(state.RenderSignature(reset.AddSeconds(-1)), state.RenderSignature(reset));
        var expiry = state.Snapshots.Values.SelectMany(s => s.Reserve?.Credits ?? []).Min(c => c.ExpiresAt)!.Value;
        Assert.NotEqual(state.RenderSignature(expiry.AddSeconds(-1)), state.RenderSignature(expiry));
    }

    [Fact]
    public void NewObservationsAndAccountEditsInvalidateDetailCallbacks()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var state = new DashboardState();
        state.LoadDemo(now);
        var original = state.RenderSignature(now);
        var id = state.Accounts[0].Id;
        state.Snapshots[id] = state.Snapshots[id] with { ObservedAt = now.AddMinutes(1), PlanType = "plus" };
        var updated = state.RenderSignature(now);
        Assert.NotEqual(original, updated);
        state.Accounts[0] = state.Accounts[0] with { Nickname = "Renamed" };
        Assert.NotEqual(updated, state.RenderSignature(now));
    }
}
