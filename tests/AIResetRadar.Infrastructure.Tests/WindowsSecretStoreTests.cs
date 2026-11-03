using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Diagnostics;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsSecretStoreTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "ScroogeCredentialTests", Guid.NewGuid().ToString("N"));
    readonly string id = Guid.NewGuid().ToString("N");
    string CredentialPath => Path.Combine(directory, id + ".bin");

    [Fact]
    public async Task RoundTripStoresCiphertextAndRestrictsAccess()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSecretStore(directory);
        const string token = "synthetic-secret-not-a-provider-token";
        await store.SaveAsync(id, token);
        Assert.Equal(token, await new WindowsSecretStore(directory).ReadAsync(id));
        Assert.DoesNotContain(token, Encoding.UTF8.GetString(await File.ReadAllBytesAsync(CredentialPath)));
        Assert.False(File.Exists(CredentialPath + ".tmp"));
        var user = WindowsIdentity.GetCurrent().User!;
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var acl = new DirectoryInfo(directory).GetAccessControl();
        Assert.True(acl.AreAccessRulesProtected);
        Assert.All(acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(), rule =>
            Assert.True(rule.IdentityReference.Equals(user) || rule.IdentityReference.Equals(system)));
    }

    [Fact]
    public async Task ExistingDpapiRecordRemainsReadable()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(directory);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes("legacy-synthetic"),
            Encoding.UTF8.GetBytes("AIResetRadar.Credentials.v1"), DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(CredentialPath, bytes);
        Assert.Equal("legacy-synthetic", await new WindowsSecretStore(directory).ReadAsync(id));
    }

    [Fact]
    public async Task CorruptOrOversizedRecordRequiresReconnectWithoutLeakingContent()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSecretStore(directory);
        await store.SaveAsync(id, "good");
        await File.WriteAllTextAsync(CredentialPath, "synthetic-secret-corrupt-file");
        var error = await Assert.ThrowsAsync<ProviderAccessException>(() => store.ReadAsync(id));
        Assert.Equal(SourceHealth.AuthenticationRequired, error.Health);
        Assert.DoesNotContain("synthetic", error.Message);
        using (var file = File.OpenWrite(CredentialPath)) file.SetLength(WindowsSecretStore.MaximumEncryptedBytes + 1);
        await Assert.ThrowsAsync<ProviderAccessException>(() => store.ReadAsync(id));
    }

    [Fact]
    public async Task RejectedSavePreservesGoodCredential()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSecretStore(directory);
        await store.SaveAsync(id, "good");
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(id, new string('x', WindowsSecretStore.MaximumPlaintextBytes + 1)));
        Assert.Equal("good", await store.ReadAsync(id));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("../outside", "bad"));
    }

    [Fact]
    public async Task InterruptedCiphertextDoesNotReplaceGoodRecordAndRemovalDeletesBoth()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSecretStore(directory);
        await store.SaveAsync(id, "good");
        await File.WriteAllBytesAsync(CredentialPath + ".tmp", [1, 2, 3]);
        Assert.Equal("good", await store.ReadAsync(id));
        store.Remove(id);
        Assert.Null(await store.ReadAsync(id));
        Assert.False(File.Exists(CredentialPath + ".tmp"));
        store.Remove(id);
    }

    [Fact]
    public async Task SeparateStoreInstancesSerializeWritesToTheSameCredential()
    {
        if (!OperatingSystem.IsWindows()) return;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => new WindowsSecretStore(directory).SaveAsync(id, "token-" + i)));
        Assert.StartsWith("token-", await new WindowsSecretStore(directory).ReadAsync(id));
        Assert.False(File.Exists(CredentialPath + ".tmp"));
    }

    [Fact]
    public async Task FailedReplacementPreservesOldCiphertextAndRemovesTemporaryFile()
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSecretStore(directory);
        await store.SaveAsync(id, "original-synthetic");
        var original = await File.ReadAllBytesAsync(CredentialPath);
        using (var held = new FileStream(CredentialPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = await Record.ExceptionAsync(() => store.SaveAsync(id, "replacement-synthetic"));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(CredentialPath));
        Assert.False(File.Exists(CredentialPath + ".tmp"));
        Assert.Equal("original-synthetic", await store.ReadAsync(id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnotherProcessHoldingLeasePreventsTemporaryWriteUntilRelease(bool wholeOperation)
    {
        if (!OperatingSystem.IsWindows()) return;
        var store = new WindowsSecretStore(directory);
        await store.SaveAsync(id, "original-synthetic");
        if (wholeOperation) { using var initial = await store.AcquireOperationAsync(id); }
        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["SCROOGE_TEST_LOCK_FILE"] = CredentialPath + (wholeOperation ? ".operation.lock" : ".lock");
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference='Stop'; $f=[IO.File]::Open($env:SCROOGE_TEST_LOCK_FILE,'Open','ReadWrite','None'); try { [Console]::WriteLine('ready'); [Console]::ReadLine() | Out-Null } finally { $f.Dispose() }" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task? save = null;
        try
        {
            Assert.Equal("ready", await process.StandardOutput.ReadLineAsync(timeout.Token));
            async Task SaveUnderOperationAsync()
            {
                using var operation = await store.AcquireOperationAsync(id, timeout.Token);
                await store.SaveAsync(id, "replacement-synthetic");
            }
            save = wholeOperation ? SaveUnderOperationAsync() : store.SaveAsync(id, "replacement-synthetic");
            Assert.False(save.IsCompleted);
            Assert.False(File.Exists(CredentialPath + ".tmp"));
            await process.StandardInput.WriteLineAsync("release");
            await process.StandardInput.FlushAsync();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode);
            await save.WaitAsync(timeout.Token);
            Assert.Equal("replacement-synthetic", await store.ReadAsync(id));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            if (save is not null) await save;
        }
    }

    [Fact]
    public async Task BusyLeaseHasBoundedWaitAndSupportsCancellation()
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(directory);
        var path = CredentialPath + ".lock";
        using var first = await WindowsCredentialLease.AcquireAsync(path);
        await Assert.ThrowsAsync<IOException>(() => WindowsCredentialLease.AcquireAsync(path, timeout: TimeSpan.Zero));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WindowsCredentialLease.AcquireAsync(path, canceled.Token));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
