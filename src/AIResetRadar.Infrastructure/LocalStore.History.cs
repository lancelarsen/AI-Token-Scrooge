using AIResetRadar.Core;
using Microsoft.Data.Sqlite;

namespace AIResetRadar.Infrastructure;

public sealed partial class LocalStore
{
    /// <summary>Returns new/extended runs after a revision, or the initial bounded range.</summary>
    public async Task<HistoryPage> HistoryPageAsync(string accountId, DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive, long? afterRevision = null, bool includePreceding = true, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (toInclusive < fromInclusive) throw new ArgumentException("The history interval ends before it starts.", nameof(toInclusive));
        await gate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct);
            using var transaction = connection.BeginTransaction(deferred: true);
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM history_clock";
            var revision = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
            var definitions = await DefinitionsAsync(connection, accountId, ct, transaction, afterRevision);
            command.Parameters.AddWithValue("$account", accountId);
            command.Parameters.AddWithValue("$from", fromInclusive.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$to", toInclusive.ToUnixTimeMilliseconds());
            // last_at=0 only occurs on malformed/legacy rows inserted by older importers.
            var last = "CASE WHEN last_at=0 THEN observed_at ELSE last_at END";
            command.CommandText = $"SELECT observed_at,value FROM history WHERE account_id=$account AND observed_at <= $to AND {last} >= $from";
            if (afterRevision is { } cursor)
            {
                command.CommandText += " AND revision > $revision";
                command.Parameters.AddWithValue("$revision", cursor);
            }
            command.CommandText += " ORDER BY observed_at";
            List<HistoryInterval> intervals = [];
            await using (var reader = await command.ExecuteReaderAsync(ct))
                while (await reader.ReadAsync(ct))
                {
                    var key = reader.GetInt64(0);
                    var interval = Unpack(key, reader.GetString(1), accountId, definitions);
                    if (interval is null)
                    {
                        var gap = new QuotaSnapshot(accountId, "", "", DateTimeOffset.FromUnixTimeMilliseconds(key), [], null,
                            SourceHealth.Stale, "History Unavailable");
                        interval = new(key, gap, gap, 1);
                    }
                    intervals.Add(interval);
                }
            if (includePreceding && afterRevision is null)
            {
                command.Parameters.Clear(); command.Parameters.AddWithValue("$account", accountId);
                command.Parameters.AddWithValue("$from", fromInclusive.ToUnixTimeMilliseconds());
                command.CommandText = $"SELECT observed_at,value FROM history WHERE account_id=$account AND {last} <= $from ORDER BY {last} DESC";
                await using var reader = await command.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                    if (Unpack(reader.GetInt64(0), reader.GetString(1), accountId, definitions) is { } interval && interval.Last.ObservedAt < fromInclusive)
                    { if (!intervals.Any(i => i.Key == interval.Key)) intervals.Insert(0, interval); break; }
            }
            return new(revision, intervals);
        }
        finally { gate.Release(); }
    }

    /// <summary>Measured endpoints of runs, inclusive and chronological, plus an optional preceding anchor.</summary>
    public async Task<IReadOnlyList<QuotaSnapshot>> HistoryAsync(string accountId, DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive, bool includePreceding = false, CancellationToken ct = default)
    {
        var page = await HistoryPageAsync(accountId, fromInclusive, toInclusive, includePreceding: includePreceding, ct: ct);
        var endpoints = page.Intervals.SelectMany(i => i.Endpoints()).OrderBy(s => s.ObservedAt).ToArray();
        var result = endpoints.Where(s => s.ObservedAt >= fromInclusive && s.ObservedAt <= toInclusive).ToList();
        if (includePreceding && endpoints.LastOrDefault(s => s.ObservedAt < fromInclusive && !string.IsNullOrEmpty(s.Subject)) is { } previous)
            result.Insert(0, previous);
        return result;
    }

    async Task WriteSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction, QuotaSnapshot snapshot, CancellationToken ct, int pollingMinutes = 2)
    {
        await WritePackedSnapshotAsync(connection, transaction, snapshot, ct, pollingMinutes);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "DELETE FROM history WHERE last_at < $cutoff AND (last_at <> 0 OR observed_at < $cutoff)";
        command.Parameters.AddWithValue("$cutoff", HistoryRetention.Cutoff(clock.GetUtcNow()).ToUnixTimeMilliseconds());
        if (await command.ExecuteNonQueryAsync(ct) > 0)
        {
            command.Parameters.Clear(); command.CommandText = PruneDefinitionsSql;
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
