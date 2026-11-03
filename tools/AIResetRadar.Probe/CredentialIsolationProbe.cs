using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using AIResetRadar.Infrastructure;

internal static class CredentialIsolationProbe
{
    const string Sentinel = "Scrooge synthetic credential isolation fixture v1";
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AIResetRadar.Credentials.v1");
    sealed record Manifest(int Version, string OwnerSid, string CredentialId, string CiphertextHash);

    internal static async Task<int> RunAsync(string mode, string directory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This check requires Windows.");
        try { return mode == "--prepare-credential-isolation" ? await PrepareAsync(directory) : Verify(directory, mode == "--verify-credential-other-user"); }
        catch
        {
            Console.WriteLine("Credential isolation check could not complete. No credential contents or exception details were printed.");
            return 1;
        }
    }

    [SupportedOSPlatform("windows")]
    static async Task<int> PrepareAsync(string directory)
    {
        var root = Path.GetFullPath(directory);
        var parent = Path.GetFullPath(Path.Combine("artifacts", "native-qa"));
        if (!string.Equals(Path.GetDirectoryName(root), parent, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(root), "N", out _))
            throw new InvalidOperationException("Use a fresh GUID-named fixture under artifacts/native-qa.");
        RejectRedirects(root);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("The fixture path already exists.");
        var owner = WindowsIdentity.GetCurrent().User ?? throw new UnauthorizedAccessException();
        var acl = new DirectorySecurity(); acl.SetOwner(owner); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { owner, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        // Only this new synthetic exchange folder is readable by other standard users.
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.ReadAndExecute,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(root).Create(acl);
        var id = Guid.NewGuid().ToString("N");
        var credentials = Path.Combine(root, "Credentials");
        await new WindowsSecretStore(credentials).SaveAsync(id, Sentinel);
        // Represents a CLI-created plaintext child inheriting the same production directory policy.
        await File.WriteAllTextAsync(Path.Combine(credentials, ".credentials.json"), "{\"synthetic\":true}");
        var encrypted = await File.ReadAllBytesAsync(Path.Combine(credentials, id + ".bin"));
        await File.WriteAllBytesAsync(Path.Combine(root, "synthetic-ciphertext.bin"), encrypted);
        await File.WriteAllTextAsync(Path.Combine(root, "fixture.json"), JsonSerializer.Serialize(new Manifest(1, owner.Value, id, Convert.ToHexString(SHA256.HashData(encrypted)))));
        Console.WriteLine("Synthetic isolation fixture created. Only the test manifest and copied ciphertext are shared; the production secret-store directory remains restricted.");
        return Verify(root, false);
    }

    [SupportedOSPlatform("windows")]
    static int Verify(string directory, bool otherUser)
    {
        var root = Path.GetFullPath(directory); RejectRedirects(root);
        var manifestPath = Path.Combine(root, "fixture.json"); RejectRedirects(manifestPath);
        if (new FileInfo(manifestPath).Length > 4096) throw new InvalidDataException();
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath)) ?? throw new InvalidDataException();
        if (manifest.Version != 1 || !Guid.TryParseExact(manifest.CredentialId, "N", out _)) throw new InvalidDataException();
        var identity = WindowsIdentity.GetCurrent();
        var differentUser = identity.User?.Value != manifest.OwnerSid;
        var elevatedAdministrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        if (otherUser && (!differentUser || elevatedAdministrator))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { Status = "NotRun", Reason = "Requires a different, non-elevated standard Windows user", DifferentUser = differentUser, ElevatedAdministrator = elevatedAdministrator }));
            return 2;
        }
        if (!otherUser && differentUser) throw new UnauthorizedAccessException();
        var encryptedPath = Path.Combine(root, "synthetic-ciphertext.bin"); RejectRedirects(encryptedPath);
        if (new FileInfo(encryptedPath).Length is <= 0 or > 131072) throw new InvalidDataException();
        var encrypted = File.ReadAllBytes(encryptedPath);
        if (Convert.ToHexString(SHA256.HashData(encrypted)) != manifest.CiphertextHash) throw new InvalidDataException();
        var privateRoot = Path.Combine(root, "Credentials");
        var ciphertextAccessible = CanRead(Path.Combine(privateRoot, manifest.CredentialId + ".bin"));
        var plaintextAccessible = CanRead(Path.Combine(privateRoot, ".credentials.json"));
        var decryptSucceeded = false;
        try
        {
            var decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            try { decryptSucceeded = Encoding.UTF8.GetString(decrypted) == Sentinel; }
            finally { CryptographicOperations.ZeroMemory(decrypted); }
        }
        catch (CryptographicException) { }
        var passed = otherUser ? !ciphertextAccessible && !plaintextAccessible && !decryptSucceeded : ciphertextAccessible && plaintextAccessible && decryptSucceeded;
        Console.WriteLine(JsonSerializer.Serialize(new { Mode = otherUser ? "DifferentUser" : "Owner", DifferentUser = differentUser,
            ElevatedAdministrator = elevatedAdministrator, ProtectedCiphertextAccessible = ciphertextAccessible,
            ProtectedPlaintextAccessible = plaintextAccessible, DpapiDecryptSucceeded = decryptSucceeded, Passed = passed }));
        return passed ? 0 : 1;
    }

    static bool CanRead(string path)
    {
        try
        {
            RejectRedirects(path);
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (UnauthorizedAccessException) { return false; }
        // Missing files and other I/O failures are inconclusive, never proof of denied access.
    }

    static void RejectRedirects(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            try { if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected fixture path."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
    }
}
