namespace AIResetRadar.Infrastructure;

/// <summary>Deletes one app-owned CLI profile after the provider has confirmed logout.</summary>
internal static class OwnedProfileDirectory
{
    internal static void Delete(string dataDirectory, string provider, string credentialId)
    {
        if (!Guid.TryParseExact(credentialId, "N", out var id)) throw new ArgumentException("Invalid credential identity.");
        var child = provider switch { "OpenAI" => "Profiles", "Claude" => "ClaudeProfiles", "Devin" => "DevinProfiles", _ => throw new ArgumentException("Unknown profile provider.") };
        var root = Path.GetFullPath(Path.Combine(dataDirectory, child));
        var profile = Path.GetFullPath(Path.Combine(root, id.ToString("N")));
        if (!string.Equals(Path.GetDirectoryName(profile), root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Profile path is outside its owned directory.");
        ValidatePath(profile);
        if (!Directory.Exists(profile)) return;
        // Preflight the entire tree before deleting anything. Redirected descendants are
        // left for explicit recovery; cleanup must never traverse their targets.
        ValidateTree(profile);
        DeleteTree(profile);
    }

    static void ValidatePath(string path)
    {
        if (OperatingSystem.IsWindows()) WindowsCredentialDirectory.RejectReparsePoints(path);
        else
            for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
                if (File.Exists(current) || Directory.Exists(current))
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Profile cleanup cannot use redirected paths.");
    }

    static void ValidateTree(string path)
    {
        ValidatePath(path);
        if (OperatingSystem.IsWindows()) WindowsCredentialDirectory.RequireCurrentOwner(path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            ValidatePath(entry);
            if (Directory.Exists(entry)) ValidateTree(entry);
            else if (OperatingSystem.IsWindows()) WindowsCredentialDirectory.RequireCurrentOwner(entry);
        }
    }

    static void DeleteTree(string path)
    {
        ValidatePath(path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            ValidatePath(entry);
            if (Directory.Exists(entry)) DeleteTree(entry);
            else
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    // CLI-managed caches may include read-only Git pack files. The whole
                    // owned tree has passed preflight; recheck before changing this file.
                    ValidatePath(entry);
                    if (OperatingSystem.IsWindows()) WindowsCredentialDirectory.RequireCurrentOwner(entry);
                    File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
                }
                File.Delete(entry);
            }
        }
        Directory.Delete(path, false);
    }
}
