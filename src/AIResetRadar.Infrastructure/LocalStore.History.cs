using System.Text.Json;
using AIResetRadar.Core;
using Microsoft.Data.Sqlite;

namespace AIResetRadar.Infrastructure;

public sealed partial class LocalStore
{
    /// <summary>
    /// Reads the complete inclusive interval in observation order. When requested, the
    /// nearest usable observation strictly before the interval is prepended for continuity.
    /// Unknown quota values and non-healthy source states remain intact for the caller.
    /// Unusable in-range rows become stale, identity-free gaps without quota values.
    /// </summary>
    public async Task<IReadOnlyList<QuotaSnapshot>> HistoryAsync(string accountId,
        DateTimeOffset fromInclusive, DateTimeOffset toInclusive, bool includePreceding = false,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (toInclusive < fromInclusive) throw new ArgumentException("The history interval ends before it starts.", nameof(toInclusive));
        await gate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct);
            List<QuotaSnapshot> values = [];
            if (includePreceding)
            {
                await using var previous = connection.CreateCommand();
                // Include the boundary millisecond, then compare the original timestamp:
                // snapshots retain finer precision than the SQLite millisecond key.
                previous.CommandText = "SELECT observed_at,value FROM history WHERE account_id=$account AND observed_at <= $from ORDER BY observed_at DESC";
                previous.Parameters.AddWithValue("$account", accountId);
                previous.Parameters.AddWithValue("$from", fromInclusive.ToUnixTimeMilliseconds());
                await using var reader = await previous.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    ct.ThrowIfCancellationRequested();
                    if (ReadHistorySnapshot(reader, accountId) is { } snapshot && snapshot.ObservedAt < fromInclusive)
                    { values.Add(snapshot); break; }
                }
            }
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT observed_at,value FROM history WHERE account_id=$account AND observed_at >= $from AND observed_at <= $to ORDER BY observed_at";
            command.Parameters.AddWithValue("$account", accountId);
            command.Parameters.AddWithValue("$from", fromInclusive.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$to", toInclusive.ToUnixTimeMilliseconds());
            await using var rangeReader = await command.ExecuteReaderAsync(ct);
            while (await rangeReader.ReadAsync(ct))
            {
                ct.ThrowIfCancellationRequested();
                var snapshot = ReadHistorySnapshot(rangeReader, accountId) ?? new QuotaSnapshot(
                    accountId, "", "", DateTimeOffset.FromUnixTimeMilliseconds(rangeReader.GetInt64(0)),
                    [], null, SourceHealth.Stale, "History Unavailable");
                if (snapshot.ObservedAt >= fromInclusive && snapshot.ObservedAt <= toInclusive)
                    values.Add(snapshot);
            }
            return values;
        }
        finally { gate.Release(); }
    }

    static QuotaSnapshot? ReadHistorySnapshot(SqliteDataReader reader, string accountId)
    {
        try
        {
            var snapshot = JsonSerializer.Deserialize<QuotaSnapshot>(reader.GetString(1));
            return snapshot is not null && snapshot.AccountId == accountId &&
                snapshot.ObservedAt.ToUnixTimeMilliseconds() == reader.GetInt64(0) &&
                !string.IsNullOrWhiteSpace(snapshot.Subject) && !string.IsNullOrWhiteSpace(snapshot.Scope) &&
                snapshot.Windows is not null && snapshot.Windows.All(window => window is not null && !string.IsNullOrWhiteSpace(window.Id)) &&
                (snapshot.Reserve?.Credits is not { } credits || credits.All(credit => credit is not null &&
                    !string.IsNullOrWhiteSpace(credit.Id) && !string.IsNullOrWhiteSpace(credit.Status)))
                ? snapshot : null;
        }
        catch (JsonException) { return null; }
    }

    async Task WriteSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction,
        QuotaSnapshot snapshot, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT OR REPLACE INTO history(account_id,observed_at,value) VALUES($account,$observed,$value); DELETE FROM history WHERE observed_at < $cutoff;";
        command.Parameters.AddWithValue("$account", snapshot.AccountId);
        command.Parameters.AddWithValue("$observed", snapshot.ObservedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(snapshot));
        command.Parameters.AddWithValue("$cutoff", HistoryRetention.Cutoff(clock.GetUtcNow()).ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(ct);
    }
}
