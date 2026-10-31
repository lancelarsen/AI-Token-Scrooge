namespace AIResetRadar.Core;

public static class ReconnectPolicy
{
    public static ProviderAccount Accept(ProviderAccount original, ProviderAccount replacement)
    {
        if (original.Id != replacement.Id || original.Provider != replacement.Provider || original.Subject != replacement.Subject ||
            original.Scope != "unresolved" && !original.ScopeEquals(replacement.Scope))
            throw new InvalidDataException("This is a different account or workspace. Reconnect the original account, or add a separate connection.");
        return replacement with { Enabled = original.Enabled, PollingMinutes = original.EffectivePollingMinutes };
    }
}

// Immutable account objects act as generations: replacing even an equal record invalidates its reads.
public sealed class AccountRefreshLease(ProviderAccount account)
{
    public bool IsCurrent(ProviderAccount? current) => ReferenceEquals(account, current) && current.Enabled;
}
