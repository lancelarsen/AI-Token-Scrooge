using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Channels;
using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

/// <summary>Current-user pipe broker. Each fresh read is correlated with one live, cancelable request.</summary>
public sealed class ChromeBillingBridge : IOpenAiBillingBrowser, IAsyncDisposable
{
    readonly CancellationTokenSource lifetime = new();
    readonly ConcurrentDictionary<string, Pending> pending = new();
    readonly string pipeName;
    readonly Task server;
    Channel<byte[]>? outgoing;
    int connected;
    public ChromeBillingBridge(string pipeName = ChromeBridgeProtocol.PipeName)
    { this.pipeName = pipeName; server = Task.Run(ServeAsync); }

    public async Task<OpenAiBillingReading> ReadAsync(string profile, bool interactive, string? expectedSubject,
        string? expectedOrganization, CancellationToken ct)
    {
        if (Volatile.Read(ref connected) == 0)
            throw new ProviderAccessException("Open Chrome with the Scrooge API Credits extension installed, then try again.", SourceHealth.Offline);
        var id = Guid.NewGuid().ToString("N");
        var p = new Pending(expectedSubject, expectedOrganization);
        pending[id] = p;
        try
        {
            var message = JsonSerializer.SerializeToUtf8Bytes(new { type = "read", id, interactive,
                subject = expectedSubject, organization = expectedOrganization }, ChromeBridgeProtocol.Json);
            if (outgoing?.Writer.TryWrite(message) != true) throw new IOException();
            return await p.Completion.Task.WaitAsync(ct);
        }
        finally
        {
            pending.TryRemove(id, out _);
            outgoing?.Writer.TryWrite(JsonSerializer.SerializeToUtf8Bytes(new { type = "cancel", id }));
        }
    }

    async Task ServeAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime.Token);
                using var session = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var queue = Channel.CreateBounded<byte[]>(64);
                outgoing = queue;
                Volatile.Write(ref connected, 1);
                var writer = Task.Run(async () =>
                {
                    await foreach (var bytes in queue.Reader.ReadAllAsync(session.Token))
                        await ChromeBridgeProtocol.WriteFrameAsync(pipe, bytes, session.Token);
                });
                var reader = ReceiveAsync(pipe, session.Token);
                try { await await Task.WhenAny(writer, reader); }
                finally
                {
                    Volatile.Write(ref connected, 0);
                    outgoing = null;
                    session.Cancel(); pipe.Dispose();
                    try { await Task.WhenAll(writer, reader); } catch (Exception) { /* No raw input is logged. */ }
                    foreach (var p in pending.Values) p.Completion.TrySetException(new ProviderAccessException(
                        "Chrome disconnected. Open Chrome with the Scrooge extension and refresh.", SourceHealth.Offline));
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { try { await Task.Delay(500, lifetime.Token); } catch (OperationCanceledException) { break; } }
        }
    }

    async Task ReceiveAsync(Stream pipe, CancellationToken ct)
    {
        while (await ChromeBridgeProtocol.ReadFrameAsync(pipe, ct) is { } bytes)
        {
            var result = JsonSerializer.Deserialize<ChromeBillingResult>(bytes, ChromeBridgeProtocol.Json)
                ?? throw new InvalidDataException();
            if (result.Type != "result" || !Guid.TryParseExact(result.Id, "N", out _)) throw new InvalidDataException();
            using var document = JsonDocument.Parse(bytes);
            if (result.Error is null)
            {
                if (!document.RootElement.TryGetProperty("reading", out var reading) || reading.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException();
                foreach (var name in new[] { "subject", "organization", "granted", "used", "available", "grants" })
                    if (!reading.TryGetProperty(name, out _)) throw new InvalidDataException();
            }
            if (!pending.TryGetValue(result.Id, out var request)) continue; // Canceled, retired, or previous process.
            try { request.Completion.TrySetResult(ChromeBridgeProtocol.Validate(result, request.Subject, request.Organization)); }
            catch (Exception e) when (e is ProviderAccessException or OverflowException)
            { request.Completion.TrySetException(e is ProviderAccessException ? e : new ProviderAccessException("Invalid API credit amounts.", SourceHealth.Unsupported)); }
        }
    }

    public async ValueTask DisposeAsync()
    { await lifetime.CancelAsync(); await server; lifetime.Dispose(); }
    sealed class Pending(string? subject, string? organization)
    {
        public string? Subject { get; } = subject;
        public string? Organization { get; } = organization;
        public TaskCompletionSource<OpenAiBillingReading> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
