using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AIResetRadar.Core;
namespace AIResetRadar.Infrastructure;

public sealed class ProviderAccessException(string message, SourceHealth health, DateTimeOffset? retryAt = null) : Exception(message)
{
    public SourceHealth Health { get; } = health;
    public DateTimeOffset? RetryAt { get; } = retryAt;
}

public sealed class GitHubUsageClient(HttpClient http, TimeProvider clock)
{
    public async Task<(string Subject, string Login)> IdentifyAsync(string token, CancellationToken ct)
    {
        var user = await GetAsync("user", token, ct);
        if (!user.TryGetProperty("id", out var id) || !id.TryGetInt64(out var value) || ProviderMapping.Text(user, "login") is not { } login)
            throw new InvalidDataException("GitHub did not return an account identity.");
        return (value.ToString(System.Globalization.CultureInfo.InvariantCulture), login);
    }
    public async Task<QuotaSnapshot> ReadAsync(ProviderAccount account, string token, CancellationToken ct)
    {
        var identity = await IdentifyAsync(token, ct);
        if (identity.Subject != account.Subject) throw new ProviderAccessException("This token belongs to a different GitHub account. Reconnect the account.", SourceHealth.AuthenticationRequired);
        var now = clock.GetUtcNow(); var login = Uri.EscapeDataString(identity.Login);
        var kind = account.Unit == UsageUnit.Requests ? "premium_request" : "ai_credit";
        var path = account.Scope.StartsWith("organization:", StringComparison.Ordinal) ?
            $"organizations/{Uri.EscapeDataString(account.Scope[13..])}/settings/billing/{kind}/usage?year={now.Year}&month={now.Month}&user={login}" :
            $"users/{login}/settings/billing/{kind}/usage?year={now.Year}&month={now.Month}";
        return ProviderMapping.GitHub(account, await GetAsync(path, token, ct), now);
    }
    async Task<JsonElement> GetAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri("https://api.github.com/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("AIResetRadar/0.1"); request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var retry = response.Headers.RetryAfter?.Date ?? (response.Headers.RetryAfter?.Delta is { } delay ? clock.GetUtcNow() + delay : (DateTimeOffset?)null);
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new ProviderAccessException("GitHub sign-in expired. Reconnect this account.", SourceHealth.AuthenticationRequired);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            throw new ProviderAccessException("GitHub billing access is unavailable. Personal tokens need Plan read permission; organization billing needs authorized administration access.", SourceHealth.PermissionRequired, retry);
        if ((int)response.StatusCode == 429) throw new ProviderAccessException("GitHub asked Radar to wait before refreshing.", SourceHealth.Stale, retry ?? clock.GetUtcNow().AddMinutes(5));
        if (!response.IsSuccessStatusCode) throw new ProviderAccessException("GitHub usage is temporarily unavailable. Last known values are retained.", SourceHealth.Offline);
        if (response.Content.Headers.ContentLength > 5_000_000) throw new InvalidDataException("GitHub report exceeded the monitor's size limit.");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        return document.RootElement.Clone();
    }
}
