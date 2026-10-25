using System.Collections.Concurrent;
namespace AIResetRadar.Core;

public sealed record NotificationActivation(string Action, string AccountId, string? WindowId);

public sealed class NotificationActivationInbox
{
    readonly ConcurrentQueue<NotificationActivation> pending = new();
    public void Enqueue(NotificationActivation activation) => pending.Enqueue(activation);
    public void Drain(bool accountsLoaded, Action<NotificationActivation>? dispatch)
    {
        if (!accountsLoaded || dispatch is null) return;
        while (pending.TryDequeue(out var activation)) dispatch(activation);
    }
}
