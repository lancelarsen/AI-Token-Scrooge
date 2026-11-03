using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace AIResetRadar.Infrastructure;

/// <summary>Fallback for in-memory stores. Persistent stores must override with a shared OS lease.</summary>
internal static class CredentialOperationGate
{
    static readonly ConditionalWeakTable<ISecretStore, ConcurrentDictionary<string, SemaphoreSlim>> Stores = new();
    internal static async Task<IDisposable> AcquireAsync(ISecretStore store, string id, CancellationToken ct)
    {
        var gate = Stores.GetOrCreateValue(store).GetOrAdd(id, _ => new(1, 1));
        await gate.WaitAsync(ct);
        return new Lease(gate);
    }
    sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        SemaphoreSlim? held = gate;
        public void Dispose() => Interlocked.Exchange(ref held, null)?.Release();
    }
}
