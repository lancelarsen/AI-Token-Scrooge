using System.Text.Json;
using AIResetRadar.Core;
using Microsoft.Data.Sqlite;

namespace AIResetRadar.Infrastructure;

public interface ICredentialLifecycleJournal
{
    Task StageCredentialAsync(string provider, string credentialId, CancellationToken ct = default, CredentialProfileLocation profileLocation = CredentialProfileLocation.LegacyDataDirectory);
    Task CompleteCredentialCleanupAsync(string credentialId, CancellationToken ct = default);
}

public sealed record PendingCredential(string Provider, string CredentialId, CredentialProfileLocation ProfileLocation = CredentialProfileLocation.LegacyDataDirectory);

public sealed partial class LocalStore : ICredentialLifecycleJournal
{
    internal static PendingCredential ValidatePending(PendingCredential pending)
    {
        if (pending is null || pending.Provider is not ("OpenAI" or "Claude" or "GitHub Copilot" or "Devin") ||
            !Guid.TryParseExact(pending.CredentialId, "N", out _) || !Enum.IsDefined(pending.ProfileLocation))
            throw new InvalidDataException("Invalid pending credential record.");
        return pending;
    }

    static async Task<PendingCredential[]> ReadPendingAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT value FROM state WHERE key='pending-credentials'";
        var json = await command.ExecuteScalarAsync(ct) as string;
        return json is null ? [] : (JsonSerializer.Deserialize<PendingCredential[]>(json)
            ?? throw new InvalidDataException("Pending credential records cannot be null.")).Select(ValidatePending).ToArray();
    }

    static async Task WritePendingAsync(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<PendingCredential> pending, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT INTO state(key,value) VALUES('pending-credentials',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(pending.ToArray()));
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task StageCredentialAsync(string provider, string credentialId, CancellationToken ct = default, CredentialProfileLocation profileLocation = CredentialProfileLocation.LegacyDataDirectory)
    {
        var item = ValidatePending(new(provider, credentialId, profileLocation));
        await gate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct); using var transaction = connection.BeginTransaction();
            await using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT value FROM state WHERE key='accounts'";
            var saved = await command.ExecuteScalarAsync(ct) as string;
            var accounts = saved is null ? [] : JsonSerializer.Deserialize<ProviderAccount[]>(saved)
                ?? throw new InvalidDataException("Saved accounts cannot be null.");
            if (accounts.Any(a => (a.CredentialId ?? a.Id) == credentialId))
                throw new InvalidOperationException("A new sign-in cannot reuse an active credential.");
            var pending = await ReadPendingAsync(connection, transaction, ct);
            if (pending.Any(p => p.CredentialId == credentialId && (p.Provider != provider || p.ProfileLocation != profileLocation)))
                throw new InvalidDataException("Pending credential provider or location changed.");
            await WritePendingAsync(connection, transaction, pending.Where(p => p.CredentialId != credentialId).Append(item), ct);
            if (profileLocation == CredentialProfileLocation.LocalSecurityDirectory)
            {
                command.Parameters.Clear(); command.CommandText = "PRAGMA user_version=4";
                await command.ExecuteNonQueryAsync(ct);
            }
            ct.ThrowIfCancellationRequested(); transaction.Commit();
        }
        finally { gate.Release(); }
    }

    public async Task CompleteCredentialCleanupAsync(string credentialId, CancellationToken ct = default)
    {
        if (!Guid.TryParseExact(credentialId, "N", out _)) throw new ArgumentException("Invalid credential identity.", nameof(credentialId));
        await gate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(ct); using var transaction = connection.BeginTransaction();
            var pending = await ReadPendingAsync(connection, transaction, ct);
            await WritePendingAsync(connection, transaction, pending.Where(p => p.CredentialId != credentialId), ct);
            ct.ThrowIfCancellationRequested(); transaction.Commit();
        }
        finally { gate.Release(); }
    }
}
