using System.Diagnostics;
using System.Text.Json;
using AIResetRadar.Core;
using AIResetRadar.Infrastructure;

namespace AIResetRadar.App.Services;

public sealed record UpdateCheck(DateTimeOffset CheckedAt, AppRelease? Release);

public sealed class AppUpdateService(LocalStore store, HttpClient? client = null, TimeProvider? clock = null)
{
    readonly GitHubUpdates github = new(client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) });
    readonly TimeProvider time = clock ?? TimeProvider.System;
    readonly SemaphoreSlim operation = new(1, 1);
    bool loaded;
    DateTimeOffset retryAfter;
    string? stageId;
    public static BuildVersion CurrentVersion { get; } = ReadVersion();
    public string Root { get; } = UpdateInstallation.RootFor(AppContext.BaseDirectory);
    public UpdateCheck? LastCheck { get; private set; }
    public AppRelease? Available => LastCheck?.Release;
    public bool Busy { get; private set; }
    public bool Ready => stageId is not null;
    public string Status { get; private set; } = "Not Checked Yet";
    public event Action? Changed;

    static BuildVersion ReadVersion()
    {
        var version = typeof(AppUpdateService).Assembly.GetName().Version?.ToString();
        return BuildVersion.TryParse(version, out var parsed) ? parsed : new(2026, 11, 3, 0);
    }

    public async Task CheckAsync(bool automatic, bool enabled = true)
    {
        if (!await operation.WaitAsync(0)) return;
        try
        {
            if (!loaded)
            {
                LastCheck = await store.ReadAsync<UpdateCheck>("app-update-check");
                if (LastCheck?.Release is { } cached && !GitHubUpdates.IsValid(cached)) LastCheck = null;
                if (LastCheck?.Release is { } saved && saved.Version.CompareTo(CurrentVersion) <= 0) LastCheck = LastCheck with { Release = null };
                if (LastCheck is not null) Status = Available is { } available ? $"{available.Version} Is Available" : "Up to Date";
                loaded = true;
            }
            var now = time.GetUtcNow();
            if (Ready || automatic && !UpdateSchedule.IsDue(enabled, now, LastCheck?.CheckedAt, retryAfter)) return;
            Busy = true; Status = "Checking GitHub"; Notify();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var release = await github.FindAsync(CurrentVersion, timeout.Token);
            var updated = new UpdateCheck(time.GetUtcNow(), release);
            await store.WriteAsync("app-update-check", updated);
            LastCheck = updated;
            Status = release is null ? "Up to Date" : $"{release.Version} Is Available";
        }
        catch { retryAfter = time.GetUtcNow().AddMinutes(30); Status = "Could Not Check GitHub - Try Again Later"; }
        finally { Busy = false; operation.Release(); Notify(); }
    }

    public async Task DownloadAsync()
    {
        if (!await operation.WaitAsync(0)) return;
        string? candidate = null;
        var downloading = false;
        try
        {
            if (Ready || Available is not { } release) return;
            Busy = true; Status = "Downloading Update"; Notify();
            if (!File.Exists(UpdateInstallation.Launcher(Root))) throw new IOException("Launcher missing.");
            candidate = Guid.NewGuid().ToString("N");
            var stage = UpdateInstallation.Stage(Root, candidate);
            Directory.CreateDirectory(stage);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            downloading = true;
            var progress = new Progress<double>(fraction => { if (downloading) { Status = $"Downloading Update: {fraction:P0}"; Notify(); } });
            await github.DownloadAsync(release, UpdatePackage.Child(stage, "package.zip"), progress, timeout.Token);
            downloading = false;
            Status = "Verifying Update"; Notify();
            await Task.Run(() => UpdatePackage.ExtractAsync(UpdatePackage.Child(stage, "package.zip"), UpdatePackage.Child(stage, "verified"), release, timeout.Token), timeout.Token);
            stageId = candidate;
            Status = "Update Ready to Restart";
        }
        catch
        {
            Status = "Update Could Not Be Downloaded or Verified - Try Again";
            if (candidate is not null) { try { UpdateInstallation.RemoveOwnedTree(Root, UpdateInstallation.Stage(Root, candidate)); } catch { } }
        }
        finally { downloading = false; Busy = false; operation.Release(); Notify(); }
    }

    public async Task<bool> PrepareRestartAsync(string dataDirectory)
    {
        if (!await operation.WaitAsync(0)) return false;
        Process? helper = null;
        try
        {
            if (stageId is null || Available is not { } release) return false;
            Busy = true; Status = "Preparing Restart"; Notify();
            var stage = UpdateInstallation.Stage(Root, stageId);
            var args = UpdateInstallation.RestartArguments(Environment.GetCommandLineArgs().Skip(1), dataDirectory);
            using var parent = Process.GetCurrentProcess();
            var handoff = new UpdateHandoff(stageId, release, parent.Id, parent.StartTime.ToUniversalTime().Ticks, args);
            await File.WriteAllTextAsync(UpdatePackage.Child(stage, "handoff.json"), JsonSerializer.Serialize(handoff));
            var start = new ProcessStartInfo(UpdateInstallation.Launcher(Root)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Root };
            start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(stageId);
            helper = Process.Start(start) ?? throw new IOException("Updater did not start.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            while (!File.Exists(UpdatePackage.Child(stage, "ready.txt")))
            {
                if (helper.HasExited) throw new IOException("Updater exited.");
                await Task.Delay(200, timeout.Token);
            }
            return true;
        }
        catch
        {
            if (helper is { HasExited: false }) { try { helper.Kill(); } catch { } }
            stageId = null;
            Status = "Restart Could Not Be Prepared - Download the Update Again";
            return false;
        }
        finally { helper?.Dispose(); Busy = false; operation.Release(); Notify(); }
    }

    void Notify() => Changed?.Invoke();
}
