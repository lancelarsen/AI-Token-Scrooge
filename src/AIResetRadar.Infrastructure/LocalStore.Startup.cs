using System.Text.Json;
using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

public sealed record SavedMonitoringState(ProviderAccount[] Accounts, QuotaSnapshot[] Snapshots,
    ProviderAccount[] Retired, AlertEvent[] Alerts, Dictionary<string, DateTimeOffset> Snoozed);

public sealed partial class LocalStore
{
    public async Task<SavedMonitoringState> LoadMonitoringStateAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT key,value FROM state WHERE key IN ('accounts','snapshots','retired','alerts','snoozed','pending-credentials')";
            using var reader = await command.ExecuteReaderAsync(ct);
            Dictionary<string, string> values = [];
            while (await reader.ReadAsync(ct)) values.Add(reader.GetString(0), reader.GetString(1));
            T Read<T>(string key, T fallback) => values.TryGetValue(key, out var json)
                ? JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException($"Saved {key} cannot be null.") : fallback;
            var accounts = Read<ProviderAccount[]>("accounts", []);
            var activeCredentials = accounts.Select(a => a.CredentialId ?? a.Id).ToHashSet(StringComparer.Ordinal);
            var abandoned = Read<PendingCredential[]>("pending-credentials", []).Select(ValidatePending)
                .Where(p => !activeCredentials.Contains(p.CredentialId))
                .Select(p => new ProviderAccount(p.CredentialId, p.Provider, "", "", "", Enabled: false, CredentialId: p.CredentialId, ProfileLocation: p.ProfileLocation));
            return new(accounts, Read<QuotaSnapshot[]>("snapshots", []),
                Read<ProviderAccount[]>("retired", []).Concat(abandoned).DistinctBy(a => a.CredentialId ?? a.Id).ToArray(), Read<AlertEvent[]>("alerts", []),
                Read<Dictionary<string, DateTimeOffset>>("snoozed", []));
        }
        finally { gate.Release(); }
    }
}
