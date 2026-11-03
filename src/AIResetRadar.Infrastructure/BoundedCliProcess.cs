using System.Diagnostics;
using System.Text;

namespace AIResetRadar.Infrastructure;

internal static class BoundedCliProcess
{
    internal sealed record Result(int ExitCode, string Output);

    internal static async Task<Result> RunAsync(ProcessStartInfo start, TimeSpan timeout, CancellationToken ct,
        int outputLimit = 2_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputLimit);
        ct.ThrowIfCancellationRequested();
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stopped.CancelAfter(timeout);
        using var process = Process.Start(start) ?? throw new IOException("Provider CLI could not start.");
        var stdout = ReadAsync(process.StandardOutput, true, outputLimit, stopped);
        var stderr = ReadAsync(process.StandardError, false, outputLimit, stopped);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(stopped.Token));
            return new(process.ExitCode, await stdout);
        }
        finally
        {
            await stopped.CancelAsync();
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) when (process.HasExited) { }
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    static async Task<string> ReadAsync(TextReader reader, bool retain, int limit, CancellationTokenSource stopped)
    {
        var buffer = new char[4096];
        var output = retain ? new StringBuilder() : null;
        var total = 0;
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), stopped.Token)) != 0)
            {
                if (count > limit - total) throw new InvalidDataException("Provider output exceeded the monitor's limit.");
                total += count;
                output?.Append(buffer, 0, count);
            }
            return output?.ToString() ?? "";
        }
        catch { await stopped.CancelAsync(); throw; }
        finally { Array.Clear(buffer); }
    }
}
