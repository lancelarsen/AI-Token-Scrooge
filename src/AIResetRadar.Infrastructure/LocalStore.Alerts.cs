using System.Text.Json;
using AIResetRadar.Core;
using Microsoft.Data.Sqlite;
namespace AIResetRadar.Infrastructure;

public sealed partial class LocalStore
{
    public Task<bool> TryRecordAlertAsync(AlertEvent alert, CancellationToken ct = default) => ChangeAlertHistoryAsync(events => AddAlert(events, alert), ct);

    bool AddAlert(List<AlertEvent> events, AlertEvent alert)
    {
        if (alert.CreatedAt < HistoryRetention.Cutoff(clock.GetUtcNow()) || events.Any(e => e.Key == alert.Key)) return false;
        events.Add(alert with { Delivered = false }); return true;
    }

    public Task RecordAlertDeliveryAsync(string key, bool delivered, CancellationToken ct = default) => ChangeAlertHistoryAsync(events =>
    {
        var index = events.FindIndex(e => e.Key == key);
        if (index < 0 || !delivered || events[index].Delivered) return false;
        events[index] = events[index] with { Delivered = true }; return true;
    }, ct);

    async Task<bool> ChangeAlertHistoryAsync(Func<List<AlertEvent>, bool> change, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct);
            using var transaction = connection.BeginTransaction();
            var events = await ReadAlertsAsync(connection, transaction, ct);
            if (!change(events)) return false;
            await WriteAlertsAsync(connection, transaction, events, ct); transaction.Commit(); return true;
        }
        finally { gate.Release(); }
    }
    static async Task<List<AlertEvent>> ReadAlertsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT value FROM state WHERE key='alerts'";
        return await command.ExecuteScalarAsync(ct) is string json ? JsonSerializer.Deserialize<List<AlertEvent>>(json) ?? [] : [];
    }
    static async Task WriteAlertsAsync(SqliteConnection connection, SqliteTransaction transaction, List<AlertEvent> events, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO state(key,value) VALUES('alerts',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(events)); await command.ExecuteNonQueryAsync(ct);
    }
}
