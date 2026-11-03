using System.Runtime.Versioning;
using System.Text;
using System.Diagnostics;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialMigrationTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "ScroogeMigrationTests", Guid.NewGuid().ToString("N"));
    readonly string id = Guid.NewGuid().ToString("N");
    string Legacy => Path.Combine(root, "legacy", "Credentials");
    string Destination
    {
        get
        {
            var data = Path.Combine(root, "custom-data");
            Directory.CreateDirectory(data);
            return WindowsMigratingSecretStore.DirectoryFor(data, Path.Combine(root, "local"));
        }
    }
    string OldFile => Path.Combine(Legacy, id + ".bin");
    string NewFile => Path.Combine(Destination, id + ".bin");
    WindowsMigratingSecretStore Store() => new(Destination, Legacy);

    [Fact]
    public async Task MigrationReadsBackProtectedCopyBeforeRetiringLegacyAndSurvivesRestart()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string token = "synthetic-migration-secret";
        await new WindowsSecretStore(Legacy).SaveAsync(id, token);
        Assert.Equal(token, await Store().ReadAsync(id));
        Assert.False(File.Exists(OldFile));
        Assert.DoesNotContain(token, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(NewFile)));
        Assert.Equal(token, await Store().ReadAsync(id));
    }

    [Fact]
    public async Task InterruptedLegacyCleanupRetriesWithoutOverwritingNewerDestination()
    {
        if (!OperatingSystem.IsWindows()) return;
        await new WindowsSecretStore(Legacy).SaveAsync(id, "legacy-synthetic");
        using (var held = new FileStream(OldFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => Store().SaveAsync(id, "newer-synthetic"));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.True(File.Exists(OldFile));
        Assert.True(File.Exists(NewFile));
        Assert.Equal("newer-synthetic", await Store().ReadAsync(id));
        Assert.False(File.Exists(OldFile));
    }

    [Fact]
    public async Task FailedRemovalKeepsDestinationUntilLegacyCanBeRetired()
    {
        if (!OperatingSystem.IsWindows()) return;
        await new WindowsSecretStore(Legacy).SaveAsync(id, "old-synthetic");
        await new WindowsSecretStore(Destination).SaveAsync(id, "new-synthetic");
        using (var held = new FileStream(OldFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => Store().Remove(id));
            Assert.True(error is IOException or UnauthorizedAccessException);
            Assert.True(File.Exists(NewFile));
        }
        Store().Remove(id);
        Assert.Null(await Store().ReadAsync(id));
        Assert.False(File.Exists(OldFile));
        Assert.False(File.Exists(NewFile));
    }

    [Fact]
    public async Task CorruptDestinationDoesNotSilentlyImportStaleLegacyCredential()
    {
        if (!OperatingSystem.IsWindows()) return;
        await new WindowsSecretStore(Legacy).SaveAsync(id, "old-synthetic");
        await new WindowsSecretStore(Destination).SaveAsync(id, "new-synthetic");
        await File.WriteAllTextAsync(NewFile, "corrupt-synthetic");
        await Assert.ThrowsAsync<ProviderAccessException>(() => Store().ReadAsync(id));
        Assert.True(File.Exists(OldFile));
    }

    [Fact]
    public void LocationIsLocalAndIsolatedBetweenDataDirectories()
    {
        if (!OperatingSystem.IsWindows()) return;
        var local = Path.Combine(root, "local");
        var data = Path.Combine(root, "external-data");
        Directory.CreateDirectory(data);
        Directory.CreateDirectory(data + "-other");
        var first = WindowsMigratingSecretStore.DirectoryFor(data, local);
        Assert.StartsWith(local + Path.DirectorySeparatorChar, first);
        Assert.Equal(first, WindowsMigratingSecretStore.DirectoryFor(data.ToUpperInvariant() + Path.DirectorySeparatorChar, local));
        Assert.NotEqual(first, WindowsMigratingSecretStore.DirectoryFor(data + "-other", local));
        Assert.Throws<ArgumentException>(() => new WindowsMigratingSecretStore(Legacy, Legacy));
        Assert.Throws<DirectoryNotFoundException>(() => WindowsMigratingSecretStore.DirectoryFor(Path.Combine(root, "missing"), local));
    }

    [Fact]
    public async Task DirectoryAliasesShareOneCredentialNamespace()
    {
        if (!OperatingSystem.IsWindows()) return;
        var data = Path.Combine(root, "data");
        var alias = Path.Combine(root, "alias");
        Directory.CreateDirectory(data);
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["SCROOGE_TEST_ALIAS"] = alias;
        start.Environment["SCROOGE_TEST_TARGET"] = data;
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path $env:SCROOGE_TEST_ALIAS -Target $env:SCROOGE_TEST_TARGET | Out-Null" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(0, process.ExitCode);
            Assert.True((File.GetAttributes(alias) & FileAttributes.ReparsePoint) != 0);
            var local = Path.Combine(root, "local");
            Assert.Equal(WindowsMigratingSecretStore.DirectoryFor(data, local),
                WindowsMigratingSecretStore.DirectoryFor(alias, local));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            if (Directory.Exists(alias)) Directory.Delete(alias); // Remove only the link, never its target.
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
