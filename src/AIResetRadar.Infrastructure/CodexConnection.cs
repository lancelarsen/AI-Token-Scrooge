using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
namespace AIResetRadar.Infrastructure;

/// <summary>Owns one isolated Codex process. No thread, inference or credit-redemption method is available.</summary>
public sealed class CodexConnection : IAsyncDisposable
{
    static readonly HashSet<string> Allowed = ["initialize", "account/read", "account/rateLimits/read", "account/login/start", "account/login/cancel", "account/logout"];
    readonly Process process;
    readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    readonly SemaphoreSlim writer = new(1, 1);
    readonly CancellationTokenSource stopped = new();
    int nextId;
    readonly Task reading, errors;
    public event Action<JsonElement>? Notification;
    CodexConnection(Process process) { this.process = process; reading = ReadAsync(); errors = DrainErrorsAsync(); }

    public static async Task<CodexConnection> StartAsync(string executable, string profile, CancellationToken ct = default)
    {
        if (!File.Exists(executable) || Path.GetExtension(executable) != ".exe") throw new FileNotFoundException("Install Codex to connect an OpenAI account.");
        Directory.CreateDirectory(profile);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = profile };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("OPENAI_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["CODEX_HOME"] = Path.GetFullPath(profile);
        foreach (var arg in new[] { "app-server", "--stdio", "-c", "cli_auth_credentials_store=\"keyring\"", "-c", "mcp_servers={}", "-c", "analytics.enabled=false" }) start.ArgumentList.Add(arg);
        var connection = new CodexConnection(Process.Start(start) ?? throw new IOException("Could not start Codex."));
        try
        {
            await connection.CallAsync("initialize", new { clientInfo = new { name = "ai_reset_radar", title = "AI Token Scrooge", version = "0.1.0" }, capabilities = new { experimentalApi = true } }, ct);
            await connection.WriteAsync(new { method = "initialized" }, ct); return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<JsonElement> CallAsync(string method, object? parameters = null, CancellationToken ct = default)
    {
        if (!Allowed.Contains(method)) throw new InvalidOperationException("This method is not allowed by the usage monitor.");
        var id = Interlocked.Increment(ref nextId);
        var result = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); pending[id] = result;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, stopped.Token); timeout.CancelAfter(TimeSpan.FromSeconds(40));
        using var registration = timeout.Token.Register(() => result.TrySetCanceled(timeout.Token));
        try { await WriteAsync(new { id, method, @params = parameters }, timeout.Token); return await result.Task; }
        finally { pending.TryRemove(id, out _); }
    }

    async Task WriteAsync(object message, CancellationToken ct)
    {
        await writer.WaitAsync(ct);
        try { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), ct); await process.StandardInput.FlushAsync(ct); }
        finally { writer.Release(); }
    }
    async Task ReadAsync()
    {
        try
        {
            await foreach (var line in BoundedLines.ReadAsync(process.StandardOutput, 2_000_000, stopped.Token))
            {
                using var json = JsonDocument.Parse(line); var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Provider response was not a protocol object.");
                if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var value) && pending.TryGetValue(value, out var completion))
                {
                    if (root.TryGetProperty("error", out _)) completion.TrySetException(new InvalidOperationException("Codex could not complete this monitoring request. Check sign-in and retry."));
                    else if (root.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
                }
                else if (root.TryGetProperty("method", out _)) Notification?.Invoke(root.Clone());
            }
        }
        catch (Exception error) when (error is OperationCanceledException or IOException or JsonException or InvalidDataException) { }
        finally
        {
            foreach (var completion in pending.Values) completion.TrySetException(new IOException("Codex connection ended. Reconnect to continue monitoring."));
            // Also cancel requests added concurrently with EOF or a protocol failure.
            await stopped.CancelAsync();
        }
    }
    async Task DrainErrorsAsync()
    {
        var buffer = new char[4096];
        try { while (await process.StandardError.ReadAsync(buffer.AsMemory(), stopped.Token) != 0) { } }
        catch (Exception error) when (error is OperationCanceledException or IOException) { }
    }
    public async ValueTask DisposeAsync()
    {
        await stopped.CancelAsync();
        try { process.StandardInput.Close(); if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); await Task.WhenAll(reading, errors).WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException) { }
        process.Dispose(); stopped.Dispose();
    }
}

public static class CodexLocator
{
    public static string? Find()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "codex.exe", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
    }
}
