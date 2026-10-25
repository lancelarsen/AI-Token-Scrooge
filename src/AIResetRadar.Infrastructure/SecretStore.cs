using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
namespace AIResetRadar.Infrastructure;

public interface ISecretStore
{
    Task SaveAsync(string id, string value);
    Task<string?> ReadAsync(string id);
    void Remove(string id);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSecretStore(string directory) : ISecretStore
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AIResetRadar.Credentials.v1");
    string PathFor(string id)
    {
        if (!Guid.TryParse(id, out var guid)) throw new ArgumentException("Invalid credential identity.", nameof(id));
        return Path.Combine(directory, guid.ToString("N") + ".bin");
    }
    public async Task SaveAsync(string id, string value)
    {
        Directory.CreateDirectory(directory);
        var path = PathFor(id); var raw = Encoding.UTF8.GetBytes(value);
        try
        {
            var encrypted = ProtectedData.Protect(raw, Entropy, DataProtectionScope.CurrentUser);
            var temporary = path + ".tmp";
            await File.WriteAllBytesAsync(temporary, encrypted); File.Move(temporary, path, true);
        }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }
    public async Task<string?> ReadAsync(string id)
    {
        var path = PathFor(id); if (!File.Exists(path)) return null;
        var raw = ProtectedData.Unprotect(await File.ReadAllBytesAsync(path), Entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(raw); }
        finally { CryptographicOperations.ZeroMemory(raw); }
    }
    public void Remove(string id) { var path = PathFor(id); if (File.Exists(path)) File.Delete(path); }
}
