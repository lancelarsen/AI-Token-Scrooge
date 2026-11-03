using System.Diagnostics;
using System.Text.Json;
using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

public sealed record UpdateHandoff(string StageId, AppRelease Release, int ProcessId, long ProcessStartedTicks, string[] Arguments);

public static class UpdateInstallation
{
    public static string RootFor(string applicationDirectory)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(applicationDirectory));
        var parent = Path.GetDirectoryName(path);
        if (parent is not null && Path.GetFileName(parent) == "Versions" && BuildVersion.TryParse(Path.GetFileName(path), out _))
            return Path.GetDirectoryName(parent)!;
        return path;
    }

    public static string Launcher(string root) => UpdatePackage.Child(root, "Launcher/Scrooge.exe");
    public static string? ActiveVersion(string root)
    {
        var pointer = UpdatePackage.Child(root, "current-version.txt");
        if (!File.Exists(pointer)) return null;
        if (new FileInfo(pointer).Length > 80) throw new InvalidDataException("Invalid installed version.");
        var value = File.ReadAllText(pointer).Trim();
        if (!BuildVersion.TryParse(value, out var version) || value != version.ToString()) throw new InvalidDataException("Invalid installed version.");
        return value;
    }

    public static string ApplicationDirectory(string root) => ActiveVersion(root) is { } version ? UpdatePackage.Child(root, "Versions/" + version) : root;

    public static Process Launch(string root, IEnumerable<string> arguments)
    {
        var directory = ApplicationDirectory(root);
        var start = new ProcessStartInfo(UpdatePackage.Child(directory, "AIResetRadar.App.exe")) { UseShellExecute = false, WorkingDirectory = directory };
        start.Environment["SCROOGE_LAUNCHED_DIRECTORY"] = Path.TrimEndingDirectorySeparator(directory);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new IOException("Scrooge could not be started.");
    }

    public static string[] RestartArguments(IEnumerable<string> original, string dataDirectory)
    {
        var result = new List<string>();
        using var args = original.GetEnumerator();
        while (args.MoveNext())
        {
            if (args.Current == "--data-dir") { args.MoveNext(); continue; }
            if (args.Current != "--tray") result.Add(args.Current);
        }
        result.Add("--data-dir"); result.Add(Path.GetFullPath(dataDirectory));
        return result.ToArray();
    }

    public static async Task<FileStream> LockAsync(string root, CancellationToken ct)
    {
        var path = UpdatePackage.Child(root, "update.lock");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { await Task.Delay(200, ct); }
        }
    }

    public static string Stage(string root, string id) => Guid.TryParseExact(id, "N", out _) ?
        UpdatePackage.Child(root, "Updates/" + id) : throw new InvalidDataException("Invalid staging identifier.");

    public static async Task<UpdateHandoff> ReadHandoffAsync(string root, string stageId, CancellationToken ct)
    {
        var path = UpdatePackage.Child(Stage(root, stageId), "handoff.json");
        if (new FileInfo(path).Length > 32768) throw new InvalidDataException("Invalid update handoff.");
        var handoff = JsonSerializer.Deserialize<UpdateHandoff>(await File.ReadAllTextAsync(path, ct)) ?? throw new InvalidDataException("Missing update handoff.");
        if (handoff.StageId != stageId || handoff.Arguments.Length > 100 || handoff.Arguments.Any(a => a.Length > 8192) ||
            !BuildVersion.TryParse(handoff.Release.Version.ToString(), out _) || handoff.Release.Size is <= 0 or > GitHubUpdates.MaximumPackageBytes ||
            handoff.Release.Sha256.Length != 64 || !handoff.Release.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid update handoff.");
        return handoff;
    }

    public static void Activate(string root, string version, string readyDirectory)
    {
        if (!BuildVersion.TryParse(version, out var next)) throw new InvalidDataException("Invalid version.");
        if (ActiveVersion(root) is { } active && BuildVersion.TryParse(active, out var current) && next.CompareTo(current) <= 0)
            throw new InvalidDataException("Update is no longer newer.");
        var destination = UpdatePackage.Child(root, "Versions/" + version);
        if (Directory.Exists(destination)) throw new IOException("Version directory already exists.");
        UpdatePackage.RequireOrdinaryPath(readyDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        Directory.Move(readyDirectory, destination);
        var pending = UpdatePackage.Child(root, "current-version.pending");
        File.WriteAllText(pending, version);
        File.Move(pending, UpdatePackage.Child(root, "current-version.txt"), overwrite: true);
    }

    public static void RemoveOwnedTree(string root, string path)
    {
        // Only updater-created staging/version directories may be removed, never the portable root.
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full)!;
        var allowedStage = parent.Equals(Path.Combine(root, "Updates"), StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(Path.GetFileName(full), "N", out _);
        var allowedVersion = parent.Equals(Path.Combine(root, "Versions"), StringComparison.OrdinalIgnoreCase) && BuildVersion.TryParse(Path.GetFileName(full), out _);
        if (!allowedStage && !allowedVersion) throw new IOException("Not an owned update directory.");
        if (!Directory.Exists(full)) return;
        UpdatePackage.RequireOrdinaryPath(full);
        void Validate(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                UpdatePackage.RequireOrdinaryPath(entry);
                if (Path.GetFileName(entry).StartsWith("radar.db", StringComparison.OrdinalIgnoreCase) ||
                    new[] { "Profiles", "ClaudeProfiles", "Credentials" }.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase))
                    throw new IOException("An update directory contains user data; it will not be removed.");
                if (Directory.Exists(entry)) Validate(entry);
            }
        }
        Validate(full);
        Directory.Delete(full, recursive: true);
    }
}
