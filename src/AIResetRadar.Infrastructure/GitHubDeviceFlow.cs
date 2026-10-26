using System.Text.Json;
using System.Text.RegularExpressions;
using AIResetRadar.Core;
namespace AIResetRadar.Infrastructure;

public sealed record GitHubAppTokens(string ClientId, string AccessToken, string RefreshToken,
    DateTimeOffset ExpiresAt, DateTimeOffset RefreshExpiresAt)
{
    public override string ToString() => "GitHub app credentials";
}

public sealed class GitHubDeviceChallenge
{
    internal GitHubDeviceChallenge(string clientId, string deviceCode, string userCode, DateTimeOffset expiresAt, long started, TimeSpan lifetime, TimeSpan interval)
    { ClientId = clientId; DeviceCode = deviceCode; UserCode = userCode; ExpiresAt = expiresAt; Started = started; Lifetime = lifetime; Interval = interval; }
    internal string ClientId { get; }
    internal string DeviceCode { get; }
    internal long Started { get; }
    internal TimeSpan Lifetime { get; }
    internal TimeSpan Interval { get; }
    public string UserCode { get; }
    public DateTimeOffset ExpiresAt { get; }
    public Uri VerificationUri => new("https://github.com/login/device");
    public override string ToString() => "GitHub sign-in request";
}

// The supplied HttpClient must disable redirects. Credentials are sent only in form bodies.
public sealed class GitHubDeviceFlow(HttpClient http, TimeProvider clock, Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public static bool ValidClientId(string? value) => value is { Length: > 0 and <= 128 } && Regex.IsMatch(value, "\\A[A-Za-z0-9_.-]+\\z", RegexOptions.CultureInvariant);

    public async Task<GitHubDeviceChallenge> BeginAsync(string clientId, CancellationToken ct)
    {
        if (!ValidClientId(clientId)) throw new InvalidDataException("GitHub browser sign-in is not configured in this build.");
        var started = clock.GetTimestamp(); var now = clock.GetUtcNow();
        var data = await PostAsync("/login/device/code", new() { ["client_id"] = clientId, ["scope"] = "offline_access" }, ct);
        ThrowProviderError(data);
        var uri = Text(data, "verification_uri"); var code = Text(data, "user_code"); var device = Text(data, "device_code");
        if (uri != "https://github.com/login/device" || code is null || !Regex.IsMatch(code, "\\A[A-Z0-9]{4}-[A-Z0-9]{4}\\z", RegexOptions.CultureInvariant) || !ValidSecret(device))
            throw new InvalidDataException("GitHub returned an invalid sign-in request. Try again.");
        var lifetime = TimeSpan.FromSeconds(Number(data, "expires_in", 1, 1800));
        var interval = TimeSpan.FromSeconds(data.TryGetProperty("interval", out _) ? Number(data, "interval", 1, 300) : 5);
        return new(clientId, device!, code, now + lifetime, started, lifetime, interval);
    }

    public async Task<GitHubAppTokens> WaitAsync(GitHubDeviceChallenge challenge, CancellationToken ct)
    {
        var interval = challenge.Interval;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (challenge.Lifetime - clock.GetElapsedTime(challenge.Started) <= interval) throw Expired();
            if (delay is not null) await delay(interval, ct); else await Task.Delay(interval, clock, ct);
            ct.ThrowIfCancellationRequested();
            if (clock.GetElapsedTime(challenge.Started) >= challenge.Lifetime) throw Expired();
            var data = await PostAsync("/login/oauth/access_token", new() {
                ["client_id"] = challenge.ClientId, ["device_code"] = challenge.DeviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }, ct);
            switch (Text(data, "error"))
            {
                case "authorization_pending": continue;
                case "slow_down":
                    var seconds = interval.TotalSeconds + 5;
                    if (data.TryGetProperty("interval", out var next) && next.ValueKind == JsonValueKind.Number && next.TryGetInt32(out var value) && value is > 0 and <= 1800) seconds = Math.Max(seconds, value);
                    interval = TimeSpan.FromSeconds(seconds); continue;
                default: ThrowProviderError(data); return ParseTokens(data, challenge.ClientId);
            }
        }
    }

    public async Task<GitHubAppTokens> RefreshAsync(GitHubAppTokens previous, CancellationToken ct)
    {
        if (!ValidClientId(previous.ClientId) || !ValidSecret(previous.RefreshToken)) throw Reconnect();
        var data = await PostAsync("/login/oauth/access_token", new() {
            ["client_id"] = previous.ClientId, ["grant_type"] = "refresh_token", ["refresh_token"] = previous.RefreshToken
        }, ct);
        ThrowProviderError(data);
        return ParseTokens(data, previous.ClientId);
    }

    GitHubAppTokens ParseTokens(JsonElement data, string clientId)
    {
        var access = Text(data, "access_token"); var refresh = Text(data, "refresh_token");
        if (!ValidSecret(access) || !ValidSecret(refresh) || !string.Equals(Text(data, "token_type"), "bearer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GitHub returned incomplete sign-in credentials. Sign in again.");
        var now = clock.GetUtcNow();
        return new(clientId, access!, refresh!, now.AddSeconds(Number(data, "expires_in", 1, 86400)), now.AddSeconds(Number(data, "refresh_token_expires_in", 1, 366 * 86400)));
    }

    async Task<JsonElement> PostAsync(string path, Dictionary<string, string> fields, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(35)); ct = timeout.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com" + path) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Accept.ParseAdd("application/json"); request.Headers.UserAgent.ParseAdd("AIResetRadar/0.1");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new ProviderAccessException("GitHub sign-in is temporarily unavailable. Try again.", SourceHealth.Offline);
        if (response.Content.Headers.ContentLength > 65536) throw new InvalidDataException("GitHub sign-in response exceeded the size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var body = new MemoryStream(); var buffer = new byte[4096]; int count;
        while ((count = await stream.ReadAsync(buffer, ct)) != 0)
        {
            if (body.Length + count > 65536) throw new InvalidDataException("GitHub sign-in response exceeded the size limit.");
            body.Write(buffer, 0, count);
        }
        try
        {
            using var document = JsonDocument.Parse(body.GetBuffer().AsMemory(0, (int)body.Length));
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("GitHub returned an invalid sign-in response. Try again.");
            return document.RootElement.Clone();
        }
        catch (JsonException) { throw new InvalidDataException("GitHub returned an invalid sign-in response. Try again."); }
    }

    internal static bool ValidSecret(string? value) => value is { Length: > 0 and <= 4096 } && value.All(c => c is >= '!' and <= '~');
    static string? Text(JsonElement data, string name) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static int Number(JsonElement data, string name, int minimum, int maximum) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= minimum && number <= maximum ? number : throw new InvalidDataException("GitHub returned invalid sign-in timing. Try again.");
    static void ThrowProviderError(JsonElement data)
    {
        var error = Text(data, "error"); if (error is null) return;
        throw new ProviderAccessException(error switch {
            "access_denied" => "GitHub sign-in was declined. You can try again.",
            "expired_token" or "token_expired" => "The GitHub sign-in code expired. Start sign-in again.",
            "device_flow_disabled" or "incorrect_client_credentials" => "GitHub browser sign-in needs the app registration configured by Radar's developer.",
            "bad_refresh_token" => "GitHub sign-in expired. Reconnect this account.",
            _ => "GitHub could not complete sign-in. Try again."
        }, SourceHealth.AuthenticationRequired);
    }
    static ProviderAccessException Expired() => new("The GitHub sign-in code expired. Start sign-in again.", SourceHealth.AuthenticationRequired);
    internal static ProviderAccessException Reconnect() => new("Reconnect GitHub to restore monitoring.", SourceHealth.AuthenticationRequired);
}
