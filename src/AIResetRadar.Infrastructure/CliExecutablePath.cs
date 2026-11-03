namespace AIResetRadar.Infrastructure;

internal static class CliExecutablePath
{
    internal static string Validate(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new FileNotFoundException("The installed provider CLI could not be located. Reinstall the official CLI and try again.");
        var full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) WindowsCredentialDirectory.RejectReparsePoints(full);
        return full;
    }
}
