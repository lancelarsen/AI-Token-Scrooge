using System.IO.Pipes;
using AIResetRadar.Infrastructure;

// Chrome supplies the caller origin. Installation pins exactly one extension ID.
// This is a local-user boundary, not protection against malicious software running as that user.
if (!OperatingSystem.IsWindows() || args.Length == 0 || args[0] != ChromeExtensionIdentity.Origin) return 1;
using var lifetime = new CancellationTokenSource();
try
{
    using var pipe = new NamedPipeClientStream(".", ChromeBridgeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
    await pipe.ConnectAsync(3000, lifetime.Token);
    async Task Relay(Stream source, Stream destination)
    {
        while (await ChromeBridgeProtocol.ReadFrameAsync(source, lifetime.Token) is { } bytes)
            await ChromeBridgeProtocol.WriteFrameAsync(destination, bytes, lifetime.Token);
    }
    var input = Relay(Console.OpenStandardInput(), pipe);
    var output = Relay(pipe, Console.OpenStandardOutput());
    await await Task.WhenAny(input, output);
    lifetime.Cancel();
    return 0;
}
catch (Exception) { return 1; } // Never write provider data or diagnostic text to stdout/stderr.
