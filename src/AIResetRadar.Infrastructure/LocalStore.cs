using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace AIResetRadar.Infrastructure;

/// <summary>Non-secret local state. Tokens belong exclusively in ISecretStore.</summary>
public sealed class LocalStore
{
    readonly string connectionString;
    readonly SemaphoreSlim gate = new(1, 1);
    public LocalStore(string path)
    {
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
            command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(value));
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
            command.CommandText = "CREATE TABLE IF NOT EXISTS state(key TEXT PRIMARY KEY,value TEXT NOT NULL); PRAGMA user_version=1;";
            await command.ExecuteNonQueryAsync(ct); return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
}
