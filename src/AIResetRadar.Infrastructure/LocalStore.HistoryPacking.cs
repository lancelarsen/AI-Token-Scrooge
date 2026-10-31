using System.Text.Json;
using AIResetRadar.Core;
using Microsoft.Data.Sqlite;

namespace AIResetRadar.Infrastructure;

public sealed partial class LocalStore
{
    static readonly JsonSerializerOptions HistoryJson = new() { IgnoreReadOnlyProperties = true };
    const string PruneDefinitionsSql = "DELETE FROM history_definitions WHERE id NOT IN (SELECT COALESCE(CASE WHEN json_valid(value) THEN json_extract(value,'$.D') END,-1) FROM history)";
    // D references shared metadata. W contains only [used, limit, reset UTC ticks].
    sealed record PackedHistory(long D, long F, long L, long? P, long? Q, long N, decimal?[][] W, int C = 2, long G = 0);
    static DateTimeOffset AtTicks(long ticks) => new(ticks, TimeSpan.Zero);

    async Task<Dictionary<long, QuotaSnapshot>> DefinitionsAsync(SqliteConnection connection, string account,
        CancellationToken ct, SqliteTransaction? transaction = null, long? afterRevision = null)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT id,value FROM history_definitions WHERE account_id=$account";
        command.Parameters.AddWithValue("$account", account);
        if (afterRevision is { } revision)
        {
            command.CommandText += " AND id IN (SELECT CASE WHEN json_valid(value) THEN json_extract(value,'$.D') END FROM history WHERE account_id=$account AND revision > $revision)";
            command.Parameters.AddWithValue("$revision", revision);
        }
        await using var reader = await command.ExecuteReaderAsync(ct);
        Dictionary<long, QuotaSnapshot> result = [];
        while (await reader.ReadAsync(ct))
            if (JsonSerializer.Deserialize<QuotaSnapshot>(reader.GetString(1)) is { } definition)
                result.Add(reader.GetInt64(0), definition);
        return result;
    }

    static HistoryInterval? Unpack(long key, string json, string account, IReadOnlyDictionary<long, QuotaSnapshot> definitions)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!doc.RootElement.TryGetProperty("D", out _))
            {
                var legacy = JsonSerializer.Deserialize<QuotaSnapshot>(json);
                return Valid(legacy, account, key) ? new(key, legacy!, legacy!, 1) : null;
            }
            var packed = JsonSerializer.Deserialize<PackedHistory>(json)!;
            if (!definitions.TryGetValue(packed.D, out var definition) || packed.L < packed.F || packed.N < 1 ||
                packed.W.Length != definition.Windows.Count || packed.W.Any(w => w.Length != 3)) return null;
            var windows = definition.Windows.Select((w, i) => w with { Used = packed.W[i][0], Limit = packed.W[i][1],
                ResetsAt = packed.W[i][2] is { } ticks ? AtTicks((long)ticks) : null }).ToArray();
            var first = definition with { ObservedAt = AtTicks(packed.F), ProviderDataAt = packed.P is { } p ? AtTicks(p) : null, Windows = windows };
            var last = first with { ObservedAt = AtTicks(packed.L), ProviderDataAt = packed.Q is { } q ? AtTicks(q) : null };
            return Valid(first, account, key) ? new(key, first, last, packed.N, packed.G) : null;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or OverflowException or NullReferenceException) { return null; }
    }

    static bool Valid(QuotaSnapshot? snapshot, string account, long key) => snapshot is not null &&
        snapshot.AccountId == account && snapshot.ObservedAt.ToUnixTimeMilliseconds() == key &&
        !string.IsNullOrWhiteSpace(snapshot.Subject) && !string.IsNullOrWhiteSpace(snapshot.Scope) &&
        snapshot.Windows is not null && snapshot.Windows.All(w => w is not null && !string.IsNullOrWhiteSpace(w.Id)) &&
        (snapshot.Reserve?.Credits is not { } credits || credits.All(c => c is not null &&
            !string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Status)));

    async Task WritePackedSnapshotAsync(SqliteConnection connection, SqliteTransaction transaction, QuotaSnapshot snapshot, CancellationToken ct, int pollingMinutes = 2)
    {
        var definition = snapshot with { ObservedAt = DateTimeOffset.UnixEpoch, ProviderDataAt = null,
            Windows = snapshot.Windows.Select(w => w with { Used = null, Limit = null, ResetsAt = null }).ToArray() };
        var json = JsonSerializer.Serialize(definition, HistoryJson);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO history_definitions(account_id,value) VALUES($account,$value); SELECT id FROM history_definitions WHERE account_id=$account AND value=$value";
        command.Parameters.AddWithValue("$account", snapshot.AccountId); command.Parameters.AddWithValue("$value", json);
        var definitionId = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        var packed = new PackedHistory(definitionId, snapshot.ObservedAt.UtcTicks, snapshot.ObservedAt.UtcTicks,
            snapshot.ProviderDataAt?.UtcTicks, snapshot.ProviderDataAt?.UtcTicks, 1,
            snapshot.Windows.Select(w => new decimal?[] { w.Used, w.Limit, w.ResetsAt?.UtcTicks }).ToArray(), Math.Clamp(pollingMinutes, 1, 30));
        command.Parameters.Clear(); command.Parameters.AddWithValue("$account", snapshot.AccountId);
        command.CommandText = "SELECT observed_at,value FROM history WHERE account_id=$account ORDER BY last_at DESC,observed_at DESC LIMIT 1";
        long key = snapshot.ObservedAt.ToUnixTimeMilliseconds();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                var previousJson = reader.GetString(1);
                PackedHistory? previous = null;
                try { previous = JsonSerializer.Deserialize<PackedHistory>(previousJson); } catch (JsonException) { }
                if (previous is { D: > 0 } && previous.D == packed.D && previous.C == packed.C &&
                    snapshot.Health is SourceHealth.Healthy or SourceHealth.Demo &&
                    packed.F - previous.L >= TimeSpan.TicksPerSecond && packed.F - previous.L <= TimeSpan.TicksPerMinute * Math.Max(15, packed.C * 3) &&
                    previous.W.Length == packed.W.Length && previous.W.Zip(packed.W).All(pair => pair.First.SequenceEqual(pair.Second)) &&
                    !snapshot.Windows.Select(w => w.ResetsAt).Concat(snapshot.Reserve?.Credits?.Select(c => c.ExpiresAt) ?? [])
                        .Any(at => at is { } boundary && boundary.UtcTicks > previous.L && boundary.UtcTicks <= packed.F))
                {
                    key = reader.GetInt64(0);
                    packed = packed with { F = previous.F, P = previous.P, N = previous.N + 1, G = Math.Max(previous.G, packed.F - previous.L) };
                }
            }
        }
        command.CommandText = "UPDATE history_clock SET revision=revision+1 RETURNING revision";
        var revision = Convert.ToInt64(await command.ExecuteScalarAsync(ct));
        command.Parameters.Clear();
        command.CommandText = "INSERT OR REPLACE INTO history(account_id,observed_at,last_at,revision,value) VALUES($account,$first,$last,$revision,$value)";
        command.Parameters.AddWithValue("$account", snapshot.AccountId); command.Parameters.AddWithValue("$first", key);
        command.Parameters.AddWithValue("$last", snapshot.ObservedAt.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$revision", revision); command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(packed));
        await command.ExecuteNonQueryAsync(ct);
    }

    async Task UpgradeHistoryAsync(SqliteConnection connection, CancellationToken ct)
    {
        // SQLite's backup API includes committed journal/WAL data. Retain a rollback copy.
        if (File.Exists(connection.DataSource) && new FileInfo(connection.DataSource).Length > 0)
        {
            var backupPath = connection.DataSource + ".before-history-v3.bak";
            if (!File.Exists(backupPath))
            {
                var temporary = backupPath + "." + Guid.NewGuid().ToString("N");
                await using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = temporary, Pooling = false }.ToString()))
                { await backup.OpenAsync(ct); connection.BackupDatabase(backup); }
                File.Move(temporary, backupPath);
            }
        }
        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "CREATE TEMP TABLE history_legacy AS SELECT account_id,observed_at,value FROM history; " +
            "ALTER TABLE history ADD COLUMN last_at INTEGER NOT NULL DEFAULT 0; ALTER TABLE history ADD COLUMN revision INTEGER NOT NULL DEFAULT 0; " +
            "CREATE TABLE history_definitions(id INTEGER PRIMARY KEY,account_id TEXT NOT NULL,value TEXT NOT NULL,UNIQUE(account_id,value)); " +
            "CREATE TABLE history_clock(revision INTEGER NOT NULL); INSERT INTO history_clock VALUES(0); " +
            "CREATE INDEX history_last ON history(account_id,last_at); CREATE INDEX history_expiry ON history(last_at); CREATE INDEX history_changes ON history(account_id,revision);";
        await command.ExecuteNonQueryAsync(ct);
        command.CommandText = "DELETE FROM history"; await command.ExecuteNonQueryAsync(ct);
        // Stream the old rows from SQLite rather than holding the full JSON history in memory.
        await using var read = connection.CreateCommand(); read.Transaction = transaction;
        read.CommandText = "SELECT account_id,observed_at,value FROM history_legacy ORDER BY account_id,observed_at";
        await using (var reader = await read.ExecuteReaderAsync(ct))
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            var row = (Account: reader.GetString(0), At: reader.GetInt64(1), Json: reader.GetString(2));
            var interval = Unpack(row.At, row.Json, row.Account, new Dictionary<long, QuotaSnapshot>());
            if (interval is not null) await WritePackedSnapshotAsync(connection, transaction, interval.First, ct);
            else
            {
                // Preserve corrupt records as explicit gaps, without trusting their contents.
                command.CommandText = "INSERT INTO history(account_id,observed_at,last_at,value) VALUES($account,$at,$at,$value)";
                command.Parameters.Clear(); command.Parameters.AddWithValue("$account", row.Account);
                command.Parameters.AddWithValue("$at", row.At); command.Parameters.AddWithValue("$value", row.Json);
                await command.ExecuteNonQueryAsync(ct);
            }
        }
        command.Parameters.Clear(); command.CommandText = "DROP TABLE history_legacy; PRAGMA user_version=3"; await command.ExecuteNonQueryAsync(ct);
        transaction.Commit();
        // Reclaim the pages freed by migration. Failure here leaves the committed data
        // valid; SQLite can reuse those pages during ordinary monitoring.
        await using var compact = connection.CreateCommand(); compact.CommandText = "VACUUM";
        try { await compact.ExecuteNonQueryAsync(ct); } catch (SqliteException) { }
    }
}
