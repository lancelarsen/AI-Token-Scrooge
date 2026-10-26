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
        var report = await GetAsync(path, token, ct);
        decimal? allowance = null;
        string? plan = null;
        if (account.Scope == "personal")
        {
            try
            {
                var entitlement = await GetAsync("copilot_internal/user", token, ct);
                allowance = ProviderMapping.GitHubAllowance(entitlement, account.Unit);
                if (entitlement.ValueKind == JsonValueKind.Object) plan = ProviderMapping.Text(entitlement, "copilot_plan");
            }
            catch (ProviderAccessException error) when (error.RetryAt is null && error.Health is SourceHealth.PermissionRequired or SourceHealth.Offline) { }
            catch (Exception error) when (error is JsonException or InvalidDataException or HttpRequestException) { }
        }
        return ProviderMapping.GitHub(account, report, now, allowance) with { PlanType = plan };
    }
    async Task<JsonElement> GetAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri("https://api.github.com/"), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("AIResetRadar/0.1"); request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var retry = response.Headers.RetryAfter?.Date ?? (response.Headers.RetryAfter?.Delta is { } delay ? clock.GetUtcNow() + delay : (DateTimeOffset?)null);
        var limited = response.StatusCode == HttpStatusCode.TooManyRequests || response.StatusCode == HttpStatusCode.Forbidden &&
            (retry is not null || response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.Contains("0"));
        if (limited)
        {
            if (retry is null && response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues) && long.TryParse(resetValues.FirstOrDefault(), out var reset) && reset is > 0 and <= 253402300799)
                retry = DateTimeOffset.FromUnixTimeSeconds(reset);
            throw new ProviderAccessException("GitHub asked Radar to wait before refreshing.", SourceHealth.Stale,
                retry > clock.GetUtcNow() ? retry : clock.GetUtcNow().AddMinutes(5));
        }
        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new ProviderAccessException("GitHub sign-in expired. Reconnect this account.", SourceHealth.AuthenticationRequired);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            throw new ProviderAccessException("GitHub billing access is unavailable. Personal tokens need Plan read permission; organization billing needs authorized administration access.", SourceHealth.PermissionRequired, retry);
        if (!response.IsSuccessStatusCode) throw new ProviderAccessException("GitHub usage is temporarily unavailable. Last known values are retained.", SourceHealth.Offline);
        if (response.Content.Headers.ContentLength > 5_000_000) throw new InvalidDataException("GitHub report exceeded the monitor's size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var body = new MemoryStream(); var buffer = new byte[16384]; int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            if (body.Length + read > 5_000_000) throw new InvalidDataException("GitHub report exceeded the monitor's size limit.");
            body.Write(buffer, 0, read);
        }
        using var document = JsonDocument.Parse(body.GetBuffer().AsMemory(0, (int)body.Length));
        return document.RootElement.Clone();
    }
}
