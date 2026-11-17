using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

/// <summary>Only normalized billing data crosses this boundary. No provider headers or login responses.</summary>
public static class ChromeBridgeProtocol
{
    public const string PipeName = "AITokenScrooge.ChromeBilling.v1";
    public const string HostName = "com.aitokenscrooge.billing";
    public const int MaximumBytes = 131072;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (await stream.ReadAsync(header.AsMemory(0, 1), ct) == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), ct);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (size is 0 or > MaximumBytes) throw new InvalidDataException("Invalid bridge frame size.");
        var bytes = new byte[(int)size];
        await stream.ReadExactlyAsync(bytes, ct);
        return bytes;
    }

    public static async Task WriteFrameAsync(Stream stream, byte[] bytes, CancellationToken ct)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw new InvalidDataException("Invalid bridge frame size.");
        var header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)bytes.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(bytes, ct);
        await stream.FlushAsync(ct);
    }

    public static OpenAiBillingReading Validate(ChromeBillingResult result, string? subject, string? organization)
    {
        if (result.Type != "result" || !Guid.TryParseExact(result.Id, "N", out _)) throw Invalid();
        if (result.Error is not null)
            throw result.Error switch
            {
                "auth" => new ProviderAccessException("Open Chrome and sign in to the original OpenAI API account and organization.", SourceHealth.AuthenticationRequired),
                "permission" => new ProviderAccessException("The signed-in Chrome account cannot read this API organization's billing.", SourceHealth.PermissionRequired),
                "rate" => new ProviderAccessException("OpenAI asked Scrooge to slow down. Try again later.", SourceHealth.Stale,
                    DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(result.RetryAfterSeconds ?? 900, 1, 86400))),
                "offline" => new ProviderAccessException("Chrome could not load the API billing page. Check Chrome and try again.", SourceHealth.Offline),
                _ => Invalid()
            };
        var r = result.Reading ?? throw Invalid();
        if (!ValidId(r.Subject) || !ValidId(r.Organization) || r.Granted < 0 || r.Used < 0 ||
            Math.Abs(r.Granted - r.Used - r.Available) > 0.000001m || r.Grants is null || r.Grants.Count > 1000 ||
            r.Grants.Any(g => g is null || !ValidId(g.Id) || g.Remaining <= 0) || r.Grants.Select(g => g.Id).Distinct().Count() != r.Grants.Count)
            throw Invalid();
        if (subject is not null && r.Subject != subject || organization is not null && r.Organization != organization)
            throw new ProviderAccessException("Chrome is signed in to a different API account or organization. Select the original organization and refresh.", SourceHealth.AuthenticationRequired);
        return r;
    }

    static bool ValidId(string? id) => id is not null && Regex.IsMatch(id, @"\A[A-Za-z0-9_-]{1,200}\z");
    static ProviderAccessException Invalid() => new("The Chrome extension did not return recognizable API credit data. Update the extension and try again.", SourceHealth.Unsupported);
}

public sealed record ChromeBillingResult(string Type, string Id, OpenAiBillingReading? Reading = null,
    string? Error = null, int? RetryAfterSeconds = null);
