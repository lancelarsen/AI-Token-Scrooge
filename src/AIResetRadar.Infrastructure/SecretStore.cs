using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
using System.Collections.Concurrent;
using AIResetRadar.Core;
namespace AIResetRadar.Infrastructure;

public interface ISecretStore
{
    Task SaveAsync(string id, string value);
    Task<string?> ReadAsync(string id);
    void Remove(string id);
    Task<IDisposable> AcquireOperationAsync(string id, CancellationToken ct = default) => CredentialOperationGate.AcquireAsync(this, id, ct);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSecretStore(string directory) : ISecretStore
{
    public async Task<IDisposable> AcquireOperationAsync(string id, CancellationToken ct = default)
    {
        var path = PathFor(id);
        WindowsCredentialDirectory.Ensure(directory);
        return await WindowsCredentialLease.AcquireAsync(path + ".operation.lock", ct);
    }
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AIResetRadar.Credentials.v1");
    internal const int MaximumPlaintextBytes = 65536;
    internal const int MaximumEncryptedBytes = 131072;
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    static SemaphoreSlim Gate(string path) => Gates.GetOrAdd(Path.GetFullPath(path), _ => new(1, 1));
    string PathFor(string id)
    {
        if (!Guid.TryParse(id, out var guid)) throw new ArgumentException("Invalid credential identity.", nameof(id));
        return Path.GetFullPath(Path.Combine(directory, guid.ToString("N") + ".bin"));
    }
    public async Task SaveAsync(string id, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Encoding.UTF8.GetByteCount(value) > MaximumPlaintextBytes) throw new ArgumentException("Credential exceeds the storage limit.");
        var path = PathFor(id); var gate = Gate(path);
        await gate.WaitAsync();
        try
        {
            WindowsCredentialDirectory.Ensure(directory);
            using var lease = await WindowsCredentialLease.AcquireAsync(path + ".lock");
            WindowsCredentialDirectory.RejectReparsePoints(path);
            var temporary = path + ".tmp";
            WindowsCredentialDirectory.RejectReparsePoints(temporary);
            // An interrupted write is ciphertext, but must not outlive removal or replace a good record.
            File.Delete(temporary);
            var raw = Encoding.UTF8.GetBytes(value);
            try
            {
                var encrypted = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        await file.WriteAsync(encrypted);
                        file.Flush(flushToDisk: true);
                    }
                    WindowsCredentialDirectory.RestrictFile(temporary);
                    File.Move(temporary, path, true);
                }
                finally { File.Delete(temporary); }
            }
            finally { CryptographicOperations.ZeroMemory(raw); }
        }
        finally { gate.Release(); }
    }
    public async Task<string?> ReadAsync(string id)
    {
        var path = PathFor(id); var gate = Gate(path);
        await gate.WaitAsync();
        try
        {
            WindowsCredentialDirectory.RejectReparsePoints(path);
            if (!Directory.Exists(directory)) return null;
            WindowsCredentialDirectory.Ensure(directory);
            using var lease = await WindowsCredentialLease.AcquireAsync(path + ".lock");
            if (!File.Exists(path)) return null;
            WindowsCredentialDirectory.RestrictFile(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 0 or > MaximumEncryptedBytes) throw Reconnect();
            var encrypted = new byte[(int)file.Length];
            await file.ReadExactlyAsync(encrypted);
            byte[] raw;
            try { raw = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser); }
            catch (CryptographicException) { throw Reconnect(); }
            try
            {
                if (raw.Length > MaximumPlaintextBytes) throw Reconnect();
                return Encoding.UTF8.GetString(raw);
            }
            finally { CryptographicOperations.ZeroMemory(raw); }
        }
        finally { gate.Release(); }
    }
    public void Remove(string id)
    {
        var path = PathFor(id); var gate = Gate(path); gate.Wait();
        try
        {
            WindowsCredentialDirectory.RejectReparsePoints(path);
            WindowsCredentialDirectory.RejectReparsePoints(path + ".tmp");
            if (!Directory.Exists(directory)) return;
            WindowsCredentialDirectory.Ensure(directory);
            using var lease = WindowsCredentialLease.AcquireAsync(path + ".lock").GetAwaiter().GetResult();
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
        finally { gate.Release(); }
    }
    static ProviderAccessException Reconnect() => new("Windows could not read the saved credential. Reconnect this account.", SourceHealth.AuthenticationRequired);
}
