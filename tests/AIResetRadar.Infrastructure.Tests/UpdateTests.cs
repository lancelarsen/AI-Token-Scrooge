using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIResetRadar.Core;
using Xunit;

namespace AIResetRadar.Infrastructure.Tests;

public sealed class UpdateTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    static readonly BuildVersion Current = new(2026, 11, 3, 1);
    static readonly BuildVersion Next = new(2026, 11, 3, 2);
    static readonly Uri NextUrl = new($"https://github.com/{GitHubUpdates.Repository}/releases/download/{Next}/AITokenScrooge-{Next}-win-x64.zip");
    public UpdateTests() => Directory.CreateDirectory(root);

    static object Release(BuildVersion version, bool draft = false, string? digest = null, string? url = null, long size = 100) => new
    {
        tag_name = version.ToString(), draft, prerelease = true,
        assets = new[] { new { name = $"AITokenScrooge-{version}-win-x64.zip", state = "uploaded", size,
            digest = digest ?? "sha256:" + new string('a', 64),
            browser_download_url = url ?? $"https://github.com/{GitHubUpdates.Repository}/releases/download/{version}/AITokenScrooge-{version}-win-x64.zip" } }
    };

    [Fact]
    public void ChoosesNewestCompletePrereleaseRegardlessOfApiOrder()
    {
        var newer = new BuildVersion(2026, 11, 3, 10);
        var json = JsonSerializer.SerializeToUtf8Bytes(new[] { Release(Next), Release(newer), Release(Current), Release(new(2027, 1, 1, 1), draft: true) });
        Assert.Equal(newer, GitHubUpdates.SelectRelease(json, Current)!.Version);
    }

    [Theory]
    [InlineData("", null, 100)]
    [InlineData("sha256:bad", null, 100)]
    [InlineData(null, "https://example.com/update.zip", 100)]
    [InlineData(null, null, 0)]
    [InlineData(null, null, 600000000)]
    public void InvalidAssetsAreNotOffered(string? digest, string? url, long size) =>
        Assert.Null(GitHubUpdates.SelectRelease(JsonSerializer.SerializeToUtf8Bytes(new[] { Release(Next, digest: digest, url: url, size: size) }), Current));

    [Fact]
    public async Task DownloadChecksBytesAndDigestWithoutCredentials()
    {
        var bytes = Encoding.UTF8.GetBytes("synthetic-package");
        var release = new AppRelease(Next, NextUrl, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
        var handler = new ResponseHandler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var github = new GitHubUpdates(new(handler));
        await github.DownloadAsync(release, Path.Combine(root, "ok.zip"), null, default);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, "ok.zip")));
        Assert.Null(handler.Authorization);
        await Assert.ThrowsAsync<InvalidDataException>(() => github.DownloadAsync(release with { Sha256 = new string('0', 64) }, Path.Combine(root, "bad.zip"), null, default));
        await Assert.ThrowsAsync<InvalidDataException>(() => github.DownloadAsync(release with { Size = bytes.Length - 1 }, Path.Combine(root, "long.zip"), null, default));
    }

    [Fact]
    public async Task DownloadRejectsRedirectOutsideGitHub()
    {
        var handler = new ResponseHandler(_ => { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new("https://example.com/file"); return response; });
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdates(new(handler)).DownloadAsync(
            new(Next, NextUrl, new string('0', 64), 10), Path.Combine(root, "never.zip"), null, default));
        Assert.Equal(1, handler.Requests);
        Assert.False(File.Exists(Path.Combine(root, "never.zip")));
    }

    [Fact]
    public async Task CachedDownloadCannotSwitchRepositories()
    {
        var handler = new ResponseHandler(_ => throw new Exception("Must not send a request"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdates(new(handler)).DownloadAsync(
            new(Next, new("https://github.com/other/repo/releases/download/v2026.11.3.002/payload.zip"), new string('a', 64), 100), Path.Combine(root, "never.zip"), null, default));
        Assert.Equal(0, handler.Requests);
    }

    (string Zip, AppRelease Release) Package(string? extra = null, string? omitted = null, string? version = null, int protocol = 1)
    {
        var zip = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            foreach (var name in UpdatePackage.RequiredFiles.Where(f => f != omitted))
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(name == "build-info.json" ? JsonSerializer.Serialize(new BuildInfo(version ?? Next.ToString(), "synthetic", protocol)) : "synthetic");
            }
            if (extra is not null) { using var writer = new StreamWriter(archive.CreateEntry(extra).Open()); writer.Write("synthetic"); }
        }
        return (zip, new(Next, new("https://github.com/synthetic"), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(zip))), new FileInfo(zip).Length));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("/escape.txt")]
    [InlineData("file:stream")]
    [InlineData("CON.txt")]
    [InlineData("folder./file.txt")]
    [InlineData("AIResetRadar.App.EXE")]
    [InlineData("DevinProfiles/example/Cookies")]
    [InlineData("devintrials/example/cache")]
    [InlineData("radar.db")]
    [InlineData("Profiles/id/auth.json")]
    public async Task RejectsUnsafeOrDuplicatePackagePaths(string extra)
    {
        var package = Package(extra);
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePackage.ExtractAsync(package.Zip, Path.Combine(root, "out"), package.Release, default));
        Assert.False(Directory.Exists(Path.Combine(root, "out")));
    }

    [Theory]
    [InlineData("Launcher/Scrooge.exe", null, 1)]
    [InlineData(null, "v2026.11.3.003", 1)]
    [InlineData(null, null, 2)]
    public async Task RejectsIncompleteOrWrongVersionPackage(string? omitted, string? version, int protocol)
    {
        var package = Package(omitted: omitted, version: version, protocol: protocol);
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdatePackage.ExtractAsync(package.Zip, Path.Combine(root, "out"), package.Release, default));
    }

    [Fact]
    public async Task ValidPackageStagesThenActivatesWithoutChangingLocalData()
    {
        var stageId = Guid.NewGuid().ToString("N");
        var stage = UpdateInstallation.Stage(root, stageId);
        Directory.CreateDirectory(stage);
        var package = Package();
        var ready = Path.Combine(stage, "ready");
        await UpdatePackage.ExtractAsync(package.Zip, ready, package.Release, default);
        File.WriteAllText(Path.Combine(root, "radar.db"), "keep settings");
        Assert.Null(UpdateInstallation.ActiveVersion(root));
        UpdateInstallation.Activate(root, Next.ToString(), ready);
        Assert.Equal(Next.ToString(), UpdateInstallation.ActiveVersion(root));
        Assert.Equal("keep settings", File.ReadAllText(Path.Combine(root, "radar.db")));
        Assert.Equal(root, UpdateInstallation.RootFor(UpdateInstallation.ApplicationDirectory(root)));
        Assert.Throws<InvalidDataException>(() => UpdateInstallation.Activate(root, Current.ToString(), ready));
        Assert.Throws<IOException>(() => UpdateInstallation.RemoveOwnedTree(root, root));
    }

    [Fact]
    public void RestartPreservesArgumentsAndResolvesTheDataDirectory()
    {
        var data = Path.Combine(root, "space in data");
        Assert.Equal(new[] { "--compact", "--data-dir", data }, UpdateInstallation.RestartArguments(["--tray", "--data-dir", "relative", "--compact"], data));
        Assert.Equal(new[] { "--data-dir", data }, UpdateInstallation.RestartArguments([], data));
    }

    [Fact]
    public void CleanupRefusesUserDataAndTraversal()
    {
        Assert.Throws<InvalidDataException>(() => UpdateInstallation.Stage(root, "../other"));
        var version = UpdatePackage.Child(root, "Versions/" + Next);
        Directory.CreateDirectory(version);
        File.WriteAllText(Path.Combine(version, "radar.db"), "preserve");
        Assert.Throws<IOException>(() => UpdateInstallation.RemoveOwnedTree(root, version));
        Assert.True(File.Exists(Path.Combine(version, "radar.db")));
    }

    public void Dispose() => Directory.Delete(root, true);

    sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Requests;
        public string? Authorization;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; Authorization = request.Headers.Authorization?.ToString(); return Task.FromResult(respond(request)); }
    }
}
