using System.Text.Json;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public sealed class CliProfileLocationTests : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "ScroogeProfileLocations", Guid.NewGuid().ToString("N"));
    readonly string id = Guid.NewGuid().ToString("N");
    string Data => Path.Combine(folder, "custom-data");
    string Security => Path.Combine(folder, "local-security");

    [Fact] public void OldSavedAccountsAndPendingEntriesKeepTheirOriginalLocation()
    {
        var account = JsonSerializer.Deserialize<ProviderAccount>("""{"Id":"old","Provider":"OpenAI","Nickname":"Synthetic","Subject":"user","Scope":"scope"}""")!;
        Assert.Equal(CredentialProfileLocation.LegacyDataDirectory, account.ProfileLocation);
        var pending = JsonSerializer.Deserialize<PendingCredential>("""{"Provider":"Claude","CredentialId":"00000000000000000000000000000001"}""")!;
        Assert.Equal(CredentialProfileLocation.LegacyDataDirectory, pending.ProfileLocation);
    }

    [Fact] public void LocationsAreExplicitAndDoNotFallBackToExistingFolders()
    {
        var paths = new CliProfilePaths(Data, Security);
        var legacy = paths.PathFor("OpenAI", id, CredentialProfileLocation.LegacyDataDirectory);
        var current = paths.PathFor("OpenAI", id, CredentialProfileLocation.LocalSecurityDirectory);
        Directory.CreateDirectory(legacy);
        Assert.Equal(Path.Combine(Security, "Profiles", id), current);
        Assert.Equal(Path.Combine(Data, "Profiles", id), legacy);
        Assert.Equal(current, paths.PathFor("OpenAI", id, CredentialProfileLocation.LocalSecurityDirectory));
        Assert.False(Directory.Exists(current)); // Resolving a reference never moves or creates a profile.
        Assert.Throws<InvalidOperationException>(() => new CliProfilePaths(Data, null).PathFor("Claude", id, CredentialProfileLocation.LocalSecurityDirectory));
        Assert.Throws<InvalidDataException>(() => paths.PathFor("Claude", id, (CredentialProfileLocation)99));
        Assert.Throws<ArgumentException>(() => paths.PathFor("Claude", "../outside", CredentialProfileLocation.LocalSecurityDirectory));
    }

    [Fact] public async Task CleanupUsesThePersistedRootAndPreservesTheOtherLocation()
    {
        var paths = new CliProfilePaths(Data, Security);
        var legacy = paths.PathFor("Claude", id, CredentialProfileLocation.LegacyDataDirectory);
        var current = paths.PathFor("Claude", id, CredentialProfileLocation.LocalSecurityDirectory);
        Directory.CreateDirectory(legacy); Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "keep"), "synthetic-old");
        File.WriteAllText(Path.Combine(current, "remove"), "synthetic-new");
        await paths.DeleteAsync("Claude", id, CredentialProfileLocation.LocalSecurityDirectory);
        Assert.True(File.Exists(Path.Combine(legacy, "keep"))); Assert.False(Directory.Exists(current));
    }

    [Fact] public async Task CanceledCleanupDoesNotDeleteEitherLocation()
    {
        var paths = new CliProfilePaths(Data, Security);
        var legacy = paths.PathFor("Claude", id, CredentialProfileLocation.LegacyDataDirectory);
        var current = paths.PathFor("Claude", id, CredentialProfileLocation.LocalSecurityDirectory);
        Directory.CreateDirectory(legacy); Directory.CreateDirectory(current);
        File.WriteAllText(Path.Combine(legacy, "keep"), "synthetic-old");
        File.WriteAllText(Path.Combine(current, "keep"), "synthetic-new");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => paths.DeleteAsync("Claude", id,
            CredentialProfileLocation.LocalSecurityDirectory, canceled.Token));
        Assert.True(File.Exists(Path.Combine(legacy, "keep")));
        Assert.True(File.Exists(Path.Combine(current, "keep")));
    }

    [Fact] public async Task BackgroundCleanupFailureRemainsRetryable()
    {
        if (!OperatingSystem.IsWindows()) return;
        var paths = new CliProfilePaths(Data, Security);
        var current = paths.PathFor("Claude", id, CredentialProfileLocation.LocalSecurityDirectory);
        Directory.CreateDirectory(current);
        var locked = Path.Combine(current, "locked"); File.WriteAllText(locked, "synthetic-only");
        using (var held = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            await Assert.ThrowsAsync<IOException>(() => paths.DeleteAsync("Claude", id, CredentialProfileLocation.LocalSecurityDirectory));
        Assert.True(File.Exists(locked));
        await paths.DeleteAsync("Claude", id, CredentialProfileLocation.LocalSecurityDirectory);
        Assert.False(Directory.Exists(current));
    }

    [Fact] public void ReconnectPreservesPreferencesAndAcceptsTheNewStorageReference()
    {
        var old = new ProviderAccount("account", "OpenAI", "Synthetic", "user", "scope", Enabled: false, CredentialId: id, PollingMinutes: 7);
        var fresh = old with { CredentialId = Guid.NewGuid().ToString("N"), ProfileLocation = CredentialProfileLocation.LocalSecurityDirectory };
        var accepted = ReconnectPolicy.Accept(old, fresh);
        Assert.Equal(fresh.CredentialId, accepted.CredentialId); Assert.Equal(fresh.ProfileLocation, accepted.ProfileLocation);
        Assert.False(accepted.Enabled); Assert.Equal(7, accepted.PollingMinutes);
        Assert.Equal(CredentialProfileLocation.LegacyDataDirectory, old.ProfileLocation);
    }

    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
