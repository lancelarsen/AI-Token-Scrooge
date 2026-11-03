using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

public sealed record AppRelease(BuildVersion Version, Uri Download, string Sha256, long Size);

/// <summary>Public release checks are separate from provider credentials and usage requests.</summary>
public sealed class GitHubUpdates(HttpClient http)
{
    public const string Repository = "lancelarsen/AI-Token-Scrooge";
    public const long MaximumPackageBytes = 512L * 1024 * 1024;
    public static bool IsValid(AppRelease release) => BuildVersion.TryParse(release.Version.ToString(), out _) && release.Version.Revision > 0 &&
        release.Size is > 0 and <= MaximumPackageBytes && release.Sha256.Length == 64 && release.Sha256.All(Uri.IsHexDigit) &&
        release.Download.AbsoluteUri == $"https://github.com/{Repository}/releases/download/{release.Version}/AITokenScrooge-{release.Version}-win-x64.zip";
    public async Task<AppRelease?> FindAsync(BuildVersion current, CancellationToken cancellationToken)
    {
        using var request = Request(new($"https://api.github.com/repos/{Repository}/releases?per_page=100"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var buffer = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(cancellationToken), buffer, 2 * 1024 * 1024, null, cancellationToken);
        return SelectRelease(buffer.ToArray(), current);
    }

    public static AppRelease? SelectRelease(byte[] json, BuildVersion current)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Invalid release list.");
        var candidates = new List<AppRelease>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (!release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String ||
                !BuildVersion.TryParse(tag.GetString(), out var version) || version.Revision == 0 || version.CompareTo(current) <= 0 ||
                tag.GetString() != version.ToString() || !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            var expected = $"AITokenScrooge-{version}-win-x64.zip";
            var matches = assets.EnumerateArray().Where(a => a.TryGetProperty("name", out var name) && name.GetString() == expected).ToArray();
            if (matches.Length != 1) continue;
            var asset = matches[0];
            if (!asset.TryGetProperty("state", out var state) || state.GetString() != "uploaded" ||
                !asset.TryGetProperty("size", out var size) || !size.TryGetInt64(out var bytes) || bytes is <= 0 or > MaximumPackageBytes ||
                !asset.TryGetProperty("digest", out var digest) || digest.ValueKind != JsonValueKind.String ||
                digest.GetString() is not { } hash || !hash.StartsWith("sha256:", StringComparison.Ordinal) ||
                hash.Length != 71 || !hash[7..].All(Uri.IsHexDigit) ||
                !asset.TryGetProperty("browser_download_url", out var download) || download.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(download.GetString(), UriKind.Absolute, out var url) ||
                url.AbsoluteUri != $"https://github.com/{Repository}/releases/download/{version}/{expected}") continue;
            candidates.Add(new(version, url, hash[7..].ToUpperInvariant(), bytes));
        }
        return candidates.OrderByDescending(c => c.Version).FirstOrDefault();
    }

    public async Task DownloadAsync(AppRelease release, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        // Revalidate persisted metadata too; a cached record is not a trusted download URL.
        if (!IsValid(release)) throw new InvalidDataException("Invalid release download.");
        var url = release.Download;
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            if (!AllowedDownload(url)) throw new InvalidDataException("Unexpected download host.");
            using var request = Request(url);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {
                url = response.Headers.Location is { } next ? new Uri(url, next) : throw new InvalidDataException("Missing download location.");
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.Size) throw new InvalidDataException("Package size changed.");
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true);
            var copied = await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(cancellationToken), output, release.Size, progress, cancellationToken);
            if (copied != release.Size) throw new InvalidDataException("Incomplete download.");
            output.Position = 0;
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(output, cancellationToken));
            if (!hash.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package checksum did not match GitHub.");
            return;
        }
        throw new InvalidDataException("Too many download redirects.");
    }

    static bool AllowedDownload(Uri uri) => uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
        (uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com");

    static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AI-Token-Scrooge", "1.0"));
        return request;
    }

    static async Task<long> CopyBoundedAsync(Stream input, Stream output, long limit, IProgress<double>? progress, CancellationToken ct)
    {
        await using (input)
        {
            var buffer = new byte[81920]; long total = 0;
            while (true)
            {
                var count = await input.ReadAsync(buffer, ct);
                if (count == 0) return total;
                total += count;
                if (total > limit) throw new InvalidDataException("Download exceeds its size limit.");
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
                progress?.Report((double)total / limit);
            }
        }
    }
}
