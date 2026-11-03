using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

internal static class WindowsTestFileSystem
{
    // Elevated runners may create Administrators-owned fixtures. Model a normal
    // user's files explicitly without relaxing the production ownership checks.
    internal static void AssignCurrentUserOwnership(string fixtureRoot)
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.GetFullPath(fixtureRoot);
        var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(root), "N", out _))
            throw new InvalidOperationException("Ownership setup requires a GUID-named temporary fixture.");
        WindowsCredentialDirectory.RejectReparsePoints(root);
        var user = WindowsIdentity.GetCurrent().User!;
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var path))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Fixture ownership setup cannot follow a redirected path.");
            FileSystemInfo entry = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
            FileSystemSecurity acl = entry is DirectoryInfo directory ? directory.GetAccessControl(AccessControlSections.Owner) : ((FileInfo)entry).GetAccessControl(AccessControlSections.Owner);
            acl.SetOwner(user);
            if (entry is DirectoryInfo dir)
            {
                dir.SetAccessControl((DirectorySecurity)acl);
                foreach (var child in Directory.EnumerateFileSystemEntries(path)) pending.Push(child);
            }
            else ((FileInfo)entry).SetAccessControl((FileSecurity)acl);
            WindowsCredentialDirectory.RequireCurrentOwner(path);
        }
    }

    internal static async Task CreateJunctionAsync(string link, string target)
    {
        var start = new ProcessStartInfo("powershell.exe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["SCROOGE_TEST_ALIAS"] = link;
        start.Environment["SCROOGE_TEST_TARGET"] = target;
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
            "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path $env:SCROOGE_TEST_ALIAS -Target $env:SCROOGE_TEST_TARGET | Out-Null" })
            start.ArgumentList.Add(arg);
        // Allow cold PowerShell startup on busy hosted runners. The subprocess is
        // still bounded, its streams are drained, and timeout kills its owned tree.
        var result = await BoundedCliProcess.RunAsync(start, TimeSpan.FromSeconds(60), default);
        Assert.Equal(0, result.ExitCode);
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
    }
}
