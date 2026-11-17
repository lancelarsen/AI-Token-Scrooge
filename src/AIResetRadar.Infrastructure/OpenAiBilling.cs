using System.Text.Json;
using System.Text.RegularExpressions;
using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

public sealed record OpenAiBillingIdentity(string Subject, IReadOnlySet<string> Organizations);
public sealed record OpenAiBillingReading(string Subject, string Organization, decimal Granted, decimal Used,
    decimal Available, IReadOnlyList<ApiCreditGrant> Grants);

/// <summary>Parses only the required fields. Never retain or log the dashboard login response.</summary>
public static class OpenAiBilling
{
    public const string Provider = "OpenAI API";
    public const string Page = "https://platform.openai.com/settings/organization/billing/credit-grants";
    public const string LoginEndpoint = "https://api.openai.com/v1/dashboard/onboarding/login";
    public const string GrantsEndpoint = "https://api.openai.com/v1/dashboard/billing/credit_grants";

    public static DateTimeOffset RetryAt(string? value, DateTimeOffset now)
    {
        var fallback = now.AddMinutes(15);
        if (!System.Net.Http.Headers.RetryConditionHeaderValue.TryParse(value, out var retry)) return fallback;
        var reported = retry.Date ?? (retry.Delta is { } delay ? now.Add(delay) : fallback);
        return reported > now ? reported : fallback;
    }

    public static OpenAiBillingIdentity Identity(JsonElement root)
    {
        try
        {
            var user = root.GetProperty("user");
            var subject = Id(user.GetProperty("id").GetString());
            var orgs = user.GetProperty("orgs").GetProperty("data").EnumerateArray()
                .Select(o => Id(o.GetProperty("id").GetString())).ToHashSet(StringComparer.Ordinal);
            if (orgs.Count == 0) throw Invalid();
            return new(subject, orgs);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
        { throw Invalid(); }
    }

    public static OpenAiBillingReading Read(OpenAiBillingIdentity identity, string organization, JsonElement root, DateTimeOffset now)
    {
        if (!identity.Organizations.Contains(organization))
            throw new ProviderAccessException("The API billing organization could not be verified. Sign in again.", SourceHealth.AuthenticationRequired);
        try
        {
            var granted = root.GetProperty("total_granted").GetDecimal();
            var used = root.GetProperty("total_used").GetDecimal();
            var available = root.GetProperty("total_available").GetDecimal();
            if (granted < 0 || used < 0 || Math.Abs(granted - used - available) > 0.000001m) throw Invalid();
            var grants = new List<ApiCreditGrant>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var items = root.GetProperty("grants").GetProperty("data");
            if (items.GetArrayLength() > 10000) throw Invalid();
            foreach (var item in items.EnumerateArray())
            {
                var id = Id(item.GetProperty("id").GetString());
                if (!ids.Add(id)) throw Invalid();
                var amount = item.GetProperty("grant_amount").GetDecimal();
                var spent = item.GetProperty("used_amount").GetDecimal();
                if (amount < 0 || spent < 0) throw Invalid();
                var expiry = Timestamp(item, "expires_at");
                var effective = Timestamp(item, "effective_at");
                var scheduled = item.GetProperty("is_scheduled").GetBoolean();
                var held = item.GetProperty("is_held_for_prepaid_transition").GetBoolean();
                if (!scheduled && !held && effective <= now && (expiry is null || expiry > now) && amount > spent)
                    grants.Add(new(id, amount - spent, expiry));
            }
            return new(identity.Subject, organization, granted, used, available, grants);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentOutOfRangeException)
        { throw Invalid(); }
    }

    public static QuotaSnapshot Snapshot(ProviderAccount account, OpenAiBillingReading reading, DateTimeOffset now)
    {
        if (account.Provider != Provider || account.Subject != reading.Subject || account.Scope != reading.Organization)
            throw new ProviderAccessException("The API billing account or organization changed. Sign in again with the original account.", SourceHealth.AuthenticationRequired);
        return new(account.Id, account.Subject, account.Scope, now,
            [new("openai-api/credits", "API Credits · USD", UsageUnit.Usd, reading.Used, reading.Granted, null,
                AllowanceSource: AllowanceProvenance.Provider)], null, SourceHealth.Healthy,
            "Full organization API credit pool, including purchased and promotional credits. Billing data may lag. Expiration does not promise a renewal grant.",
            PlanType: "api", ApiCreditGrants: reading.Grants);
    }

    static string Id(string? value) => value is not null && Regex.IsMatch(value, @"\A[A-Za-z0-9_-]{1,200}\z") ? value : throw Invalid();
    static DateTimeOffset? Timestamp(JsonElement item, string name) => item.GetProperty(name) is var value && value.ValueKind != JsonValueKind.Null
        ? DateTimeOffset.UnixEpoch.AddMilliseconds((double)(value.GetDecimal() * 1000)) : null;
    static ProviderAccessException Invalid() => new("OpenAI did not return recognizable API credit data. Check the Platform billing page.", SourceHealth.Unsupported);
}
