using System.IO.Pipes;
using System.Text.Json;
using AIResetRadar.Core;
using AIResetRadar.Infrastructure;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public sealed class ChromeBillingBridgeTests
{
    static OpenAiBillingReading Reading => new("user-fixture", "org-fixture", 50, 12.5m, 37.5m,
        [new("grant-fixture",37.5m,DateTimeOffset.UtcNow.AddDays(3))]);
    static ChromeBillingResult Result => new("result", Guid.NewGuid().ToString("N"), Reading);

    [Fact]
    public async Task FramesRejectOversizeAndTruncationAndRoundTrip()
    {
        using var stream = new MemoryStream();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Result,ChromeBridgeProtocol.Json);
        await ChromeBridgeProtocol.WriteFrameAsync(stream,bytes,default);
        stream.Position=0;
        Assert.Equal(bytes,await ChromeBridgeProtocol.ReadFrameAsync(stream,default));
        Assert.Null(await ChromeBridgeProtocol.ReadFrameAsync(stream,default));
        await Assert.ThrowsAsync<InvalidDataException>(()=>ChromeBridgeProtocol.ReadFrameAsync(new MemoryStream([255,255,255,127]),default));
        await Assert.ThrowsAsync<EndOfStreamException>(()=>ChromeBridgeProtocol.ReadFrameAsync(new MemoryStream([10,0,0,0,1]),default));
    }

    [Fact]
    public void ValidatesIdentityAccountingAndUnknownFields()
    {
        Assert.Equal(Reading.Available,ChromeBridgeProtocol.Validate(Result,"user-fixture","org-fixture").Available);
        Assert.Throws<ProviderAccessException>(()=>ChromeBridgeProtocol.Validate(Result,"different-user","org-fixture"));
        Assert.Throws<ProviderAccessException>(()=>ChromeBridgeProtocol.Validate(Result,"user-fixture","different-org"));
        Assert.Throws<ProviderAccessException>(()=>ChromeBridgeProtocol.Validate(Result with {Reading=Reading with {Available=100}},null,null));
        Assert.Throws<ProviderAccessException>(()=>ChromeBridgeProtocol.Validate(Result with {Reading=Reading with {Grants=[new("same",1,null),new("same",1,null)]}},null,null));
        var json=JsonSerializer.Serialize(Result,ChromeBridgeProtocol.Json).TrimEnd('}')+",\"cookie\":\"synthetic-secret\"}";
        Assert.Throws<JsonException>(()=>JsonSerializer.Deserialize<ChromeBillingResult>(json,ChromeBridgeProtocol.Json));
    }

    [Theory]
    [InlineData("auth",SourceHealth.AuthenticationRequired)]
    [InlineData("permission",SourceHealth.PermissionRequired)]
    [InlineData("offline",SourceHealth.Offline)]
    [InlineData("rate",SourceHealth.Stale)]
    [InlineData("other",SourceHealth.Unsupported)]
    public void MapsErrorsWithoutProviderText(string error,SourceHealth health)
    {
        var exception=Assert.Throws<ProviderAccessException>(()=>ChromeBridgeProtocol.Validate(Result with {Reading=null,Error=error},null,null));
        Assert.Equal(health,exception.Health);
    }

    [Fact]
    public async Task PipeCorrelatesFreshRequestsAndRejectsCanceledResults()
    {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var name="scrooge-test-"+Guid.NewGuid().ToString("N");
        await using var bridge=new ChromeBillingBridge(name);
        using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
        await pipe.ConnectAsync(deadline.Token);
        // Connection completion and the server's connected flag are independent scheduling events.
        Task<OpenAiBillingReading>? first=null;
        for (var i=0;i<100;i++)
        {
            first=bridge.ReadAsync("unused",false,"user-fixture","org-fixture",deadline.Token);
            if (!first.IsFaulted) break;
            await Task.Delay(10,deadline.Token);
        }
        using var request=JsonDocument.Parse((await ChromeBridgeProtocol.ReadFrameAsync(pipe,deadline.Token))!);
        var id=request.RootElement.GetProperty("id").GetString()!;
        await ChromeBridgeProtocol.WriteFrameAsync(pipe,JsonSerializer.SerializeToUtf8Bytes(Result with {Id=id},ChromeBridgeProtocol.Json),deadline.Token);
        Assert.Equal(37.5m,(await first!).Available);
        await ChromeBridgeProtocol.ReadFrameAsync(pipe,deadline.Token); // completion cancellation/cleanup

        using var cancel=new CancellationTokenSource();
        var second=bridge.ReadAsync("unused",false,"user-fixture","org-fixture",cancel.Token);
        using var secondRequest=JsonDocument.Parse((await ChromeBridgeProtocol.ReadFrameAsync(pipe,deadline.Token))!);
        var oldId=secondRequest.RootElement.GetProperty("id").GetString()!;
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>second);
        await ChromeBridgeProtocol.ReadFrameAsync(pipe,deadline.Token);
        var third=bridge.ReadAsync("unused",false,"user-fixture","org-fixture",deadline.Token);
        using var thirdRequest=JsonDocument.Parse((await ChromeBridgeProtocol.ReadFrameAsync(pipe,deadline.Token))!);
        await ChromeBridgeProtocol.WriteFrameAsync(pipe,JsonSerializer.SerializeToUtf8Bytes(Result with {Id=oldId},ChromeBridgeProtocol.Json),deadline.Token);
        Assert.False(third.IsCompleted);
        await ChromeBridgeProtocol.WriteFrameAsync(pipe,JsonSerializer.SerializeToUtf8Bytes(Result with {Id=thirdRequest.RootElement.GetProperty("id").GetString()!},ChromeBridgeProtocol.Json),deadline.Token);
        Assert.Equal(37.5m,(await third).Available);
    }
}
