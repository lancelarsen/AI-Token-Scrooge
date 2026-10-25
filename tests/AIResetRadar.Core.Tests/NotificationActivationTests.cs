using Xunit;
namespace AIResetRadar.Core.Tests;

public sealed class NotificationActivationTests
{
    [Fact] public void ColdClickWaitsForBothSavedAccountsAndAHandler()
    {
        var inbox = new NotificationActivationInbox();
        var click = new NotificationActivation("view", "account", "weekly");
        List<NotificationActivation> dispatched = [];
        inbox.Enqueue(click);
        inbox.Drain(false, dispatched.Add);
        inbox.Drain(true, null);
        Assert.Empty(dispatched);
        inbox.Drain(true, dispatched.Add);
        Assert.Equal(click, Assert.Single(dispatched));
        inbox.Drain(true, dispatched.Add);
        Assert.Single(dispatched);
    }
    [Fact] public void QueuedActionsPreserveExactAccountWindowAndOrder()
    {
        var inbox = new NotificationActivationInbox();
        NotificationActivation[] clicks = [new("view", "a", "weekly"), new("snooze", "b", null)];
        foreach (var click in clicks) inbox.Enqueue(click);
        List<NotificationActivation> dispatched = [];
        inbox.Drain(true, dispatched.Add);
        Assert.Equal(clicks, dispatched);
    }
}
