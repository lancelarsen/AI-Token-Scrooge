using System.Net;
using System.Text;
using AIResetRadar.App.Services;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public sealed class AppUpdateServiceTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    readonly Clock clock = new();
    public AppUpdateServiceTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task CheckIsPersistedAndReopeningDoesNotPollBeforeEightHours()
    {
        var handler = new Handler();
        var store = new LocalStore(Path.Combine(root, "radar.db"));
        await store.WriteAsync("settings", new RadarSettings(CheckForNewerVersions: false, SoundVolume: 46));
        var service = new AppUpdateService(store, new(handler), clock);
        await service.CheckAsync(automatic: true, enabled: false);
        Assert.Equal(0, handler.Count);
        await service.CheckAsync(automatic: false);
        Assert.Equal(1, handler.Count);
        var reopened = new AppUpdateService(new LocalStore(Path.Combine(root, "radar.db")), new(handler), clock);
        await reopened.CheckAsync(automatic: true);
        Assert.Equal(1, handler.Count);
        Assert.Equal(clock.GetUtcNow(), reopened.LastCheck!.CheckedAt);
        clock.Now = clock.Now.AddHours(8);
        await reopened.CheckAsync(automatic: true);
        Assert.Equal(2, handler.Count);
        var settings = await store.ReadAsync<RadarSettings>("settings");
        Assert.False(settings!.CheckForNewerVersions);
        Assert.Equal(46, settings.SoundVolume);
    }

    [Fact]
    public async Task FailedCheckKeepsLastSuccessfulTimeAndBacksOff()
    {
        var handler = new Handler();
        var service = new AppUpdateService(new LocalStore(Path.Combine(root, "radar.db")), new(handler), clock);
        await service.CheckAsync(automatic: false);
        var success = service.LastCheck;
        clock.Now = clock.Now.AddHours(8);
        handler.Status = HttpStatusCode.ServiceUnavailable;
        await service.CheckAsync(automatic: true);
        await service.CheckAsync(automatic: true);
        Assert.Equal(2, handler.Count);
        Assert.Equal(success, service.LastCheck);
        Assert.Contains("Could Not Check", service.Status);
        clock.Now = clock.Now.AddMinutes(30);
        handler.Status = HttpStatusCode.OK;
        await service.CheckAsync(automatic: true);
        Assert.Equal(3, handler.Count);
        Assert.Equal(clock.Now, service.LastCheck!.CheckedAt);
    }

    public void Dispose()
    {
        // Clear only this fixture's SQLite pool; other tests can still be writing.
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(root, "radar.db")}");
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    sealed class Handler : HttpMessageHandler
    {
        public int Count;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Count++; return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent("[]", Encoding.UTF8, "application/json") }); }
    }
}
