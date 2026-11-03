using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace AIResetRadar.Infrastructure;

/// <summary>Moves app-owned DPAPI records without creating a plaintext backup or trusting an incomplete copy.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMigratingSecretStore : ISecretStore
{
    readonly string directory;
    readonly WindowsSecretStore current;
    readonly WindowsSecretStore legacy;

    public Task<IDisposable> AcquireOperationAsync(string id, CancellationToken ct = default) => current.AcquireOperationAsync(id, ct);

    public WindowsMigratingSecretStore(string directory, string legacyDirectory)
    {
        this.directory = Path.GetFullPath(directory);
        if (string.Equals(this.directory, Path.GetFullPath(legacyDirectory), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Credential migration requires distinct directories.");
        current = new(directory);
        legacy = new(legacyDirectory);
    }

    public static string DirectoryFor(string dataDirectory, string localApplicationData)
    {
        // Resolve filesystem aliases (including Windows package redirection) before hashing.
        var canonical = WindowsDirectoryIdentity.Resolve(dataDirectory).ToUpperInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return Path.Combine(Path.GetFullPath(localApplicationData), "AIResetRadar", "Security", key, "Credentials");
    }

    async Task<FileStream> LeaseAsync(string id)
    {
        if (!Guid.TryParse(id, out var guid)) throw new ArgumentException("Invalid credential identity.", nameof(id));
        WindowsCredentialDirectory.Ensure(directory);
        return await WindowsCredentialLease.AcquireAsync(Path.Combine(directory, guid.ToString("N") + ".migration.lock")).ConfigureAwait(false);
    }

    public async Task<string?> ReadAsync(string id)
    {
        using var lease = await LeaseAsync(id);
        var saved = await current.ReadAsync(id);
        if (saved is null)
        {
            saved = await legacy.ReadAsync(id);
            if (saved is null) return null;
            await current.SaveAsync(id, saved);
            if (!string.Equals(saved, await current.ReadAsync(id), StringComparison.Ordinal))
                throw new IOException("Credential migration could not verify the destination. Try again.");
        }
        // Destination is authoritative after its atomic write. A failed cleanup retries on the next operation.
        legacy.Remove(id);
        return saved;
    }

    public async Task SaveAsync(string id, string value)
    {
        using var lease = await LeaseAsync(id);
        await current.SaveAsync(id, value);
        legacy.Remove(id);
    }

    public void Remove(string id) => RemoveAsync(id).GetAwaiter().GetResult();

    async Task RemoveAsync(string id)
    {
        using var lease = await LeaseAsync(id).ConfigureAwait(false);
        // Never remove the destination first: a remaining legacy file could otherwise be imported again.
        legacy.Remove(id);
        current.Remove(id);
    }
}
