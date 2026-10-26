using System.Text.Json;
using AIResetRadar.Core;
namespace AIResetRadar.Infrastructure;

public static class ProviderMapping
{
    public static QuotaSnapshot Codex(ProviderAccount account, JsonElement response, DateTimeOffset observed)
    {
        var scope = Text(response, "accountId");
        if (!string.IsNullOrEmpty(scope) && account.Scope != "unresolved" && scope != account.Scope)
            throw new InvalidDataException("The provider returned a different workspace. Reconnect this account.");
        List<QuotaWindow> windows = [];
        if (response.TryGetProperty("rateLimitsByLimitId", out var map) && map.ValueKind == JsonValueKind.Object && map.EnumerateObject().Any())
        {
            foreach (var bucket in map.EnumerateObject()) AddBucket(bucket.Name, bucket.Value);
        }
        else if (response.TryGetProperty("rateLimits", out var legacy) && legacy.ValueKind == JsonValueKind.Object)
            AddBucket(Text(legacy, "limitId") ?? "codex", legacy);
        ResetReserve? reserve = null;
        if (response.TryGetProperty("rateLimitResetCredits", out var credits) && credits.ValueKind == JsonValueKind.Object)
        {
            IReadOnlyList<ResetCredit>? details = null;
            if (credits.TryGetProperty("credits", out var list) && list.ValueKind == JsonValueKind.Array)
                details = list.EnumerateArray().Select(c => new ResetCredit(Text(c, "id") ?? throw new InvalidDataException("Reset credit identity missing."),
                    Text(c, "status") ?? "unknown", Timestamp(c, "expiresAt"), Text(c, "title"))).DistinctBy(c => c.Id).ToArray();
            reserve = new(Integer(credits, "availableCount"), details);
        }
        var message = scope is null ? "Workspace identity unavailable from provider" : null;
        if (response.TryGetProperty("ordinaryUsageAllowed", out var allowed) && allowed.ValueKind == JsonValueKind.False)
            message = "Provider reports included usage is paused";
        bool? usageAllowed = response.TryGetProperty("ordinaryUsageAllowed", out var permission) && permission.ValueKind is JsonValueKind.True or JsonValueKind.False ? permission.GetBoolean() : null;
        var hasReserve = reserve?.AvailableCount is not null || reserve?.Credits?.Count > 0;
        if (windows.Count == 0 && hasReserve) message = "Codex reported reset credits; quota windows are unavailable.";
        return new(account.Id, account.Subject, scope ?? account.Scope, observed, windows, reserve,
            windows.Count == 0 && !hasReserve ? SourceHealth.Unsupported : SourceHealth.Healthy, message, UsageAllowed: usageAllowed);

        void AddBucket(string id, JsonElement bucket)
        {
            if (bucket.ValueKind != JsonValueKind.Object) return;
            foreach (var key in new[] { "primary", "secondary" })
            {
                if (!bucket.TryGetProperty(key, out var window) || window.ValueKind != JsonValueKind.Object) continue;
                var minutes = Integer(window, "windowDurationMins");
                if (minutes == 0) minutes = null;
                var duration = minutes switch { 300 => "5-hour", 10080 => "Weekly", > 0 => $"{minutes} min", _ => key };
                var name = Text(bucket, "limitName") ?? (id == "codex" ? "Codex" : id);
                windows.Add(new($"{id}/{key}", $"{name} · {duration}", UsageUnit.Percent, Number(window, "usedPercent"),
                    100, Timestamp(window, "resetsAt"), minutes, AllowanceProvenance.Provider));
            }
        }
    }

    public static decimal? GitHubAllowance(JsonElement response, UsageUnit unit)
    {
        if (response.ValueKind != JsonValueKind.Object ||
            Text(response, "copilot_plan") is not ("individual" or "individual_pro" or "individual_max" or "individual_edu") ||
            !response.TryGetProperty("token_based_billing", out var billing) ||
            billing.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            (billing.GetBoolean() ? UsageUnit.Credits : UsageUnit.Requests) != unit ||
            !response.TryGetProperty("quota_snapshots", out var snapshots) || snapshots.ValueKind != JsonValueKind.Object ||
            !snapshots.TryGetProperty("premium_interactions", out var quota) || quota.ValueKind != JsonValueKind.Object ||
            !quota.TryGetProperty("unlimited", out var unlimited) || unlimited.ValueKind != JsonValueKind.False ||
            !quota.TryGetProperty("entitlement", out var value)) return null;
        decimal amount;
        var valid = value.ValueKind == JsonValueKind.Number ? value.TryGetDecimal(out amount) :
            decimal.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : null,
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out amount);
        return valid && amount >= 0 ? amount : null;
    }

    public static QuotaSnapshot GitHub(ProviderAccount account, JsonElement response, DateTimeOffset observed, decimal? providerAllowance = null)
    {
        if (!response.TryGetProperty("usageItems", out var items) || items.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Billing response did not include usage data.");
        decimal? used = 0;
        bool matched = false, unknownUnits = false;
        foreach (var item in items.EnumerateArray())
        {
            var unit = Text(item, "unitType");
            var selected = account.Unit == UsageUnit.Requests ? unit == "requests" : unit is "credits" or "ai-credits";
            if (unit is not ("requests" or "credits" or "ai-credits")) unknownUnits = true;
            if (selected)
            {
                var quantity = Number(item, "grossQuantity");
                if (quantity is not >= 0) throw new InvalidDataException("Billing quantity is unavailable or invalid.");
                used += quantity; matched = true;
            }
        }
        if (unknownUnits || items.GetArrayLength() > 0 && !matched) used = null;
        var utc = observed.UtcDateTime;
        var reset = new DateTimeOffset(new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1));
        var label = account.Unit == UsageUnit.Requests ? "Premium requests · Monthly" : "AI credits · Monthly";
        return new(account.Id, account.Subject, account.Scope, observed,
            [new("copilot/monthly", label, account.Unit, used, providerAllowance ?? account.Allowance, reset, null,
                providerAllowance.HasValue ? AllowanceProvenance.Provider : account.Allowance.HasValue ? AllowanceProvenance.Manual : AllowanceProvenance.Unknown)], null,
            Message: used is null ? "The report did not establish usage in the selected unit. Capacity is unavailable." : providerAllowance.HasValue ?
                "Allowance reported by GitHub; billing usage may lag." : "Automatic allowance unavailable; a user-entered allowance is used when supplied. Billing reports may lag.");
    }

    public static string? Text(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static decimal? Number(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) ? number : null;
    static int? Integer(JsonElement element, string key) => Number(element, key) is { } number && number >= 0 && number <= int.MaxValue && decimal.Truncate(number) == number ? (int)number : null;
    static DateTimeOffset? Timestamp(JsonElement element, string key)
    {
        var seconds = Number(element, key);
        return seconds is >= -62135596800 and <= 253402300799 && decimal.Truncate(seconds.Value) == seconds ? DateTimeOffset.FromUnixTimeSeconds((long)seconds.Value) : null;
    }
}
