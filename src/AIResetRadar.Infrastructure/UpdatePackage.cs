using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

public sealed record BuildInfo(string Version, string Commit, int UpdateProtocol);

public static class UpdatePackage
{
    public static readonly string[] RequiredFiles = ["AIResetRadar.App.exe", "AIResetRadar.App.dll", "AIResetRadar.App.runtimeconfig.json", "coreclr.dll", "Microsoft.UI.Xaml.dll", "github-app.json", "build-info.json", "Launcher/Scrooge.exe"];

    public static void RequireOrdinaryPath(string path)
    {
        var full = Path.GetFullPath(path);
        for (var cursor = full; cursor is not null; cursor = Path.GetDirectoryName(cursor))
        {
            if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Update paths cannot contain links or redirected directories.");
        }
    }

    public static string Child(string root, string relative)
    {
        var parts = relative.Replace('\\', '/').Split('/');
        if (parts.Length == 0 || parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') ||
            p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || p.Contains(':') ||
            new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(p.Split('.')[0], StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Invalid package path.");
        var full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package path escapes its directory.");
        RequireOrdinaryPath(full);
        return full;
    }

    public static async Task ExtractAsync(string archivePath, string destination, AppRelease release, CancellationToken ct)
    {
        RequireOrdinaryPath(archivePath); RequireOrdinaryPath(destination);
        if (Directory.Exists(destination)) throw new IOException("Staging directory already exists.");
        await using var zipFile = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (zipFile.Length != release.Size || !Convert.ToHexString(await SHA256.HashDataAsync(zipFile, ct)).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package checksum changed.");
        zipFile.Position = 0;
        using var archive = new ZipArchive(zipFile, ZipArchiveMode.Read);
        var files = archive.Entries.Where(e => e.Name.Length > 0).ToArray();
        if (files.Length is 0 or > 10000 || files.Sum(e => e.Length) > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Package is too large.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName.Replace('\\', '/').TrimEnd('/');
            _ = Child(destination, path);
            if (!names.Add(path) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Duplicate or redirected package entry.");
            if (path.Split('/').Any(p => p.Equals("Credentials", StringComparison.OrdinalIgnoreCase) || p.Equals("Profiles", StringComparison.OrdinalIgnoreCase) || p.Equals("ClaudeProfiles", StringComparison.OrdinalIgnoreCase) || p.StartsWith("radar.db", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Package contains local app data.");
        }
        if (RequiredFiles.Any(f => !names.Contains(f))) throw new InvalidDataException("Incomplete Windows package.");
        var infoEntry = archive.GetEntry("build-info.json")!;
        if (infoEntry.Length > 4096) throw new InvalidDataException("Invalid build metadata.");
        await using (var stream = infoEntry.Open())
        {
            var info = await JsonSerializer.DeserializeAsync<BuildInfo>(stream, cancellationToken: ct);
            if (info?.Version != release.Version.ToString() || info.UpdateProtocol != 1) throw new InvalidDataException("Wrong package version or update protocol.");
        }
        Directory.CreateDirectory(destination);
        foreach (var entry in files)
        {
            ct.ThrowIfCancellationRequested();
            var path = Child(destination, entry.FullName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var source = entry.Open();
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[81920];
            while (true)
            {
                var count = await source.ReadAsync(buffer, ct);
                if (count == 0) break;
                if (output.Length + count > entry.Length) throw new InvalidDataException("Package entry exceeds its declared length.");
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            if (output.Length != entry.Length) throw new InvalidDataException("Incomplete package entry.");
        }
    }
}
