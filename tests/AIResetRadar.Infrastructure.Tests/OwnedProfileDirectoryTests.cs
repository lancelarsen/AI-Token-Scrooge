using System.Diagnostics;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public sealed class OwnedProfileDirectoryTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "ScroogeProfileCleanupTests", Guid.NewGuid().ToString("N"));
    readonly string id = Guid.NewGuid().ToString("N");
    string Profile => Path.Combine(root, "ClaudeProfiles", id);

    void CreateProfile()
    {
        Directory.CreateDirectory(Path.Combine(Profile, "nested"));
        File.WriteAllText(Path.Combine(Profile, ".credentials.json"), "synthetic-only");
        File.WriteAllText(Path.Combine(Profile, "nested", "state.json"), "synthetic-only");
    }

    [Fact] public void RemovesOnlySelectedProfileAndCanRetryAfterRemoval()
    {
        CreateProfile();
        var other = Path.Combine(root, "ClaudeProfiles", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(other); File.WriteAllText(Path.Combine(other, "keep"), "unrelated");
        OwnedProfileDirectory.Delete(root, "Claude", id);
        OwnedProfileDirectory.Delete(root, "Claude", id);
        Assert.False(Directory.Exists(Profile)); Assert.True(File.Exists(Path.Combine(other, "keep")));
    }

    [Theory]
    [InlineData("Claude", "../outside")]
    [InlineData("Unknown", "00000000000000000000000000000000")]
    public void RejectsUnownedIdentityOrProvider(string provider, string identity)
    {
        CreateProfile();
        Assert.Throws<ArgumentException>(() => OwnedProfileDirectory.Delete(root, provider, identity));
        Assert.True(File.Exists(Path.Combine(Profile, ".credentials.json")));
    }

    [Fact] public void LockedFileKeepsCleanupRetryable()
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateProfile();
        using (var held = new FileStream(Path.Combine(Profile, ".credentials.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => OwnedProfileDirectory.Delete(root, "Claude", id));
        Assert.True(Directory.Exists(Profile));
        OwnedProfileDirectory.Delete(root, "Claude", id);
        Assert.False(Directory.Exists(Profile));
    }

    [Fact] public void RemovesReadOnlyCacheFilesOnlyInsideTheSelectedProfile()
    {
        if (!OperatingSystem.IsWindows()) return;
        CreateProfile();
        var cache = Path.Combine(Profile, "nested", "cache.pack");
        var outside = Path.Combine(root, "unrelated.pack");
        File.WriteAllText(cache, "synthetic-cache"); File.WriteAllText(outside, "unrelated");
        File.SetAttributes(cache, FileAttributes.ReadOnly); File.SetAttributes(outside, FileAttributes.ReadOnly);
        try
        {
            OwnedProfileDirectory.Delete(root, "Claude", id);
            Assert.False(Directory.Exists(Profile));
            Assert.Equal("unrelated", File.ReadAllText(outside));
            Assert.True((File.GetAttributes(outside) & FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            if (File.Exists(cache)) File.SetAttributes(cache, FileAttributes.Normal);
            if (File.Exists(outside)) File.SetAttributes(outside, FileAttributes.Normal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectsRedirectedProfileOrDescendantWithoutDeletingTarget(bool redirectProfile)
    {
        if (!OperatingSystem.IsWindows()) return;
        var target = Path.Combine(root, "outside"); Directory.CreateDirectory(target);
        var sentinel = Path.Combine(target, "keep"); File.WriteAllText(sentinel, "unrelated");
        if (!redirectProfile) CreateProfile();
        else Directory.CreateDirectory(Path.GetDirectoryName(Profile)!);
        var link = redirectProfile ? Profile : Path.Combine(Profile, "redirected");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["SCROOGE_TEST_ALIAS"] = link; start.Environment["SCROOGE_TEST_TARGET"] = target;
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path $env:SCROOGE_TEST_ALIAS -Target $env:SCROOGE_TEST_TARGET | Out-Null" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(0, process.ExitCode);
            Assert.Throws<IOException>(() => OwnedProfileDirectory.Delete(root, "Claude", id));
            Assert.Equal("unrelated", File.ReadAllText(sentinel));
            if (!redirectProfile) Assert.True(File.Exists(Path.Combine(Profile, ".credentials.json")));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); }
            if (Directory.Exists(link)) Directory.Delete(link, false);
        }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
