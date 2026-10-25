using System.Text.Json;
using Microsoft.Data.Sqlite;
using AIResetRadar.Core;
namespace AIResetRadar.Infrastructure;

/// <summary>Non-secret local state. Tokens belong exclusively in ISecretStore.</summary>
public sealed partial class LocalStore
{
    readonly string connectionString;
    readonly SemaphoreSlim gate = new(1, 1);
    readonly TimeProvider clock;
    public async Task<IReadOnlyList<AlertEvent>> WriteMonitoringStateAsync(IReadOnlyList<ProviderAccount> accounts, IReadOnlyList<QuotaSnapshot> snapshots, IReadOnlyList<ProviderAccount>? retired = null, IReadOnlyList<AlertEvent>? alerts = null, CancellationToken ct = default)
    {
        var values = new Dictionary<string, string> { ["accounts"] = JsonSerializer.Serialize(accounts), ["snapshots"] = JsonSerializer.Serialize(snapshots) };
        if (retired is not null) values["retired"] = JsonSerializer.Serialize(retired);
        await gate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct); using var transaction = connection.BeginTransaction();
            foreach (var item in values)
            {
                await using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "INSERT INTO state(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
                command.Parameters.AddWithValue("$key", item.Key); command.Parameters.AddWithValue("$value", item.Value); await command.ExecuteNonQueryAsync(ct);
            }
            List<AlertEvent> inserted = [];
            if (alerts is { Count: > 0 })
            {
                var events = await ReadAlertsAsync(connection, transaction, ct);
                foreach (var alert in alerts) if (AddAlert(events, alert)) inserted.Add(events[^1]);
                if (inserted.Count > 0) await WriteAlertsAsync(connection, transaction, events, ct);
            }
            transaction.Commit(); return inserted;
        }
        finally { gate.Release(); }
    }
    public async Task RecordSnapshotAsync(AIResetRadar.Core.QuotaSnapshot snapshot, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO history(account_id,observed_at,value) VALUES($account,$observed,$value); DELETE FROM history WHERE observed_at < $cutoff;";
            command.Parameters.AddWithValue("$account", snapshot.AccountId); command.Parameters.AddWithValue("$observed", snapshot.ObservedAt.ToUnixTimeMilliseconds());
            command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(snapshot)); command.Parameters.AddWithValue("$cutoff", HistoryRetention.Cutoff(clock.GetUtcNow()).ToUnixTimeMilliseconds());
            await command.ExecuteNonQueryAsync(ct);
        }
        finally { gate.Release(); }
    }
    public async Task<IReadOnlyList<AIResetRadar.Core.QuotaSnapshot>> HistoryAsync(string accountId, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct); await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM history WHERE account_id=$account ORDER BY observed_at DESC LIMIT 10000"; command.Parameters.AddWithValue("$account", accountId);
            await using var reader = await command.ExecuteReaderAsync(ct);
            List<AIResetRadar.Core.QuotaSnapshot> values = [];
            while (await reader.ReadAsync(ct)) if (JsonSerializer.Deserialize<AIResetRadar.Core.QuotaSnapshot>(reader.GetString(0)) is { } snapshot) values.Add(snapshot);
            return values;
        }
        finally { gate.Release(); }
    }
    public async Task DeleteHistoryAsync(string accountId)
    {
        await gate.WaitAsync();
        try { await using var connection = await OpenAsync(default); await using var command = connection.CreateCommand(); command.CommandText = "DELETE FROM history WHERE account_id=$account"; command.Parameters.AddWithValue("$account", accountId); await command.ExecuteNonQueryAsync(); }
        finally { gate.Release(); }
    }
    public LocalStore(string path, TimeProvider? clock = null)
    {
        this.clock = clock ?? TimeProvider.System;
        SQLitePCL.Batteries_V2.Init();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
    }
    public async Task<T?> ReadAsync<T>(string key, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM state WHERE key=$key"; command.Parameters.AddWithValue("$key", key);
            return await command.ExecuteScalarAsync(ct) is string value ? JsonSerializer.Deserialize<T>(value) : default;
        }
        finally { gate.Release(); }
    }
    public async Task WriteAsync<T>(string key, T value, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO state(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
            var json = key == "alerts" && value is IEnumerable<AlertEvent> alerts ? JsonSerializer.Serialize(HistoryRetention.PruneAlerts(alerts, clock.GetUtcNow())) : JsonSerializer.Serialize(value);
            command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$value", json);
            await command.ExecuteNonQueryAsync(ct);
        }
        finally { gate.Release(); }
    }
    async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(ct)) > 2) throw new InvalidDataException("This database was created by a newer Radar version. Update Radar before opening it.");
            command.CommandText = "CREATE TABLE IF NOT EXISTS state(key TEXT PRIMARY KEY,value TEXT NOT NULL); CREATE TABLE IF NOT EXISTS history(account_id TEXT NOT NULL,observed_at INTEGER NOT NULL,value TEXT NOT NULL,PRIMARY KEY(account_id,observed_at)); CREATE INDEX IF NOT EXISTS history_observed ON history(observed_at); PRAGMA user_version=2;";
            await command.ExecuteNonQueryAsync(ct);
            await PruneAsync(connection, ct); return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    async Task PruneAsync(SqliteConnection connection, CancellationToken ct)
    {
        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM history WHERE observed_at < $cutoff";
        command.Parameters.AddWithValue("$cutoff", HistoryRetention.Cutoff(clock.GetUtcNow()).ToUnixTimeMilliseconds()); await command.ExecuteNonQueryAsync(ct);
        command.Parameters.Clear(); command.CommandText = "SELECT value FROM state WHERE key='alerts'";
        if (await command.ExecuteScalarAsync(ct) is string json)
        {
            var alerts = JsonSerializer.Deserialize<AlertEvent[]>(json) ?? [];
            var retained = HistoryRetention.PruneAlerts(alerts, clock.GetUtcNow());
            if (retained.Length != alerts.Length)
            {
                command.CommandText = "UPDATE state SET value=$value WHERE key='alerts'"; command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(retained));
                await command.ExecuteNonQueryAsync(ct);
            }
        }
        transaction.Commit();
    }
}
