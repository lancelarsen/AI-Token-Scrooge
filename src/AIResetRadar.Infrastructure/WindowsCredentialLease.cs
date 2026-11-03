using System.Diagnostics;
using System.Runtime.Versioning;

namespace AIResetRadar.Infrastructure;

/// <summary>An OS-enforced lease shared by credential-store processes. The empty lock file is retained.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsCredentialLease
{
    internal static async Task<FileStream> AcquireAsync(string path, CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        var elapsed = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(10);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            WindowsCredentialDirectory.RejectReparsePoints(path);
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                if (elapsed.Elapsed >= limit)
                    throw new IOException("Credential storage is busy. Try again after the other operation finishes.");
                await Task.Delay(TimeSpan.FromMilliseconds(50), ct).ConfigureAwait(false);
            }
        }
    }
}
