using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

/// <summary>Uses persisted locations rather than probing folders or moving path-bound keyring profiles.</summary>
internal sealed class CliProfilePaths(string dataDirectory, string? securityDirectory)
{
    internal string RootFor(CredentialProfileLocation location) => location switch
    {
        CredentialProfileLocation.LegacyDataDirectory => Path.GetFullPath(dataDirectory),
        CredentialProfileLocation.LocalSecurityDirectory when securityDirectory is not null => Path.GetFullPath(securityDirectory),
        CredentialProfileLocation.LocalSecurityDirectory => throw new InvalidOperationException("The local credential directory is not configured."),
        _ => throw new InvalidDataException("Unknown credential profile location.")
    };

    internal string PathFor(string provider, string id, CredentialProfileLocation location)
    {
        if (!Guid.TryParseExact(id, "N", out var guid)) throw new ArgumentException("Invalid credential identity.");
        var child = provider switch { "OpenAI" => "Profiles", "Claude" => "ClaudeProfiles", _ => throw new InvalidDataException("Unknown profile provider.") };
        return Path.Combine(RootFor(location), child, guid.ToString("N"));
    }

    internal Task DeleteAsync(string provider, string id, CredentialProfileLocation location, CancellationToken ct = default)
    {
        var root = RootFor(location);
        // CLI caches can contain tens of thousands of files. Keep validation and deletion
        // off the UI thread. Cancellation can prevent the work from starting; once started,
        // await its result before completing the durable cleanup record.
        return Task.Run(() => OwnedProfileDirectory.Delete(root, provider, id), ct);
    }
}
