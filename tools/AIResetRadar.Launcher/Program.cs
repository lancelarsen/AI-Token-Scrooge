using System.Diagnostics;
using System.Runtime.InteropServices;
using AIResetRadar.Infrastructure;

// This stable helper lives outside version directories. It does not access provider data.
var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!;
try
{
    UpdatePackage.RequireOrdinaryPath(root);
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
    await using var lease = await UpdateInstallation.LockAsync(root, timeout.Token);
    if (args.Length == 2 && args[0] == "--apply-update")
    {
        var stage = UpdateInstallation.Stage(root, args[1]);
        var handoff = await UpdateInstallation.ReadHandoffAsync(root, args[1], timeout.Token);
        using var parent = Process.GetProcessById(handoff.ProcessId);
        if (parent.StartTime.ToUniversalTime().Ticks != handoff.ProcessStartedTicks ||
            !Path.GetFullPath(parent.MainModule!.FileName).Equals(Path.Combine(UpdateInstallation.ApplicationDirectory(root), "AIResetRadar.App.exe"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("The running app does not match the update request.");
        var ready = UpdatePackage.Child(stage, "ready");
        await UpdatePackage.ExtractAsync(UpdatePackage.Child(stage, "package.zip"), ready, handoff.Release, timeout.Token);
        await File.WriteAllTextAsync(UpdatePackage.Child(stage, "ready.txt"), "Ready", timeout.Token);
        await parent.WaitForExitAsync(timeout.Token);
        // Do not kill a monitoring process or force an interrupted shutdown.
        var previous = UpdateInstallation.ActiveVersion(root);
        UpdateInstallation.Activate(root, handoff.Release.Version.ToString(), ready);
        using var launched = UpdateInstallation.Launch(root, handoff.Arguments);
        if (previous is not null)
        {
            try { UpdateInstallation.RemoveOwnedTree(root, UpdatePackage.Child(root, "Versions/" + previous)); }
            catch (IOException) { /* A locked file or user data is preserved; there is no rollback path. */ }
            catch (UnauthorizedAccessException) { }
        }
        try { UpdateInstallation.RemoveOwnedTree(root, stage); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    else { using var launched = UpdateInstallation.Launch(root, args); }
    return 0;
}
catch
{
    // Never display paths, process arguments, or raw exception text in update diagnostics.
    Native.MessageBox(0, "Scrooge could not finish the update or start. Reopen Scrooge, or extract the newest GitHub release into a new folder. Your saved accounts and history are unchanged.", "AI Token Scrooge", 0x10);
    return 1;
}

static class Native
{
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    internal static extern int MessageBox(nint window, string text, string caption, uint type);
}
