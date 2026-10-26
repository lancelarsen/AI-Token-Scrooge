using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using AIResetRadar.Core;
namespace AIResetRadar.Infrastructure;

public sealed class GitHubCredentialManager(ISecretStore secrets, GitHubUsageClient usage, GitHubDeviceFlow? flow, TimeProvider clock)
{
    const string Prefix = "github-app:v1:";
    sealed record Envelope(int Version, string Subject, GitHubAppTokens Tokens, bool NeedsVerification = false, bool RenewalStarted = false);
    readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    readonly ConcurrentDictionary<string, Envelope> pending = new();
    SemaphoreSlim Gate(string id) => gates.GetOrAdd(id, _ => new(1, 1));

    public Task SaveAppAsync(string id, string subject, GitHubAppTokens tokens)
    {
        var record = new Envelope(1, subject, tokens); Validate(record);
        return secrets.SaveAsync(id, Encode(record));
    }

    public async Task<string> ReadAccessTokenAsync(ProviderAccount account, CancellationToken ct)
    {
        var id = account.CredentialId ?? account.Id; var gate = Gate(id);
        await gate.WaitAsync(ct);
        try
        {
            // A successful remote rotation must be made durable even if its caller canceled.
            if (pending.TryGetValue(id, out var replacement)) await PersistReplacementAsync(id, replacement);
            ct.ThrowIfCancellationRequested();
            var saved = await secrets.ReadAsync(id) ?? throw GitHubDeviceFlow.Reconnect();
            if (saved.Length > 32768) throw GitHubDeviceFlow.Reconnect();
            if (!saved.StartsWith(Prefix, StringComparison.Ordinal))
            {
                if (saved.StartsWith("github-app:", StringComparison.Ordinal) || !GitHubDeviceFlow.ValidSecret(saved) || saved.StartsWith('{')) throw GitHubDeviceFlow.Reconnect();
                return saved; // Existing protected PAT, not a serialized app credential.
            }
            Envelope record;
            try { record = JsonSerializer.Deserialize<Envelope>(saved[Prefix.Length..]) ?? throw GitHubDeviceFlow.Reconnect(); }
            catch (JsonException) { throw GitHubDeviceFlow.Reconnect(); }
            Validate(record);
            if (record.Subject != account.Subject || record.RenewalStarted) throw GitHubDeviceFlow.Reconnect();
            if (record.Tokens.ExpiresAt <= clock.GetUtcNow().AddMinutes(2))
            {
                if (flow is null || record.Tokens.RefreshExpiresAt <= clock.GetUtcNow()) throw GitHubDeviceFlow.Reconnect();
                ct.ThrowIfCancellationRequested();
                // A crash/lost response after this point cannot safely retry the consumed token.
                await secrets.SaveAsync(id, Encode(record with { RenewalStarted = true }));
                var rotated = await flow.RefreshAsync(record.Tokens, ct);
                record = record with { Tokens = rotated, NeedsVerification = true, RenewalStarted = false };
                pending[id] = record;
                await PersistReplacementAsync(id, record);
            }
            ct.ThrowIfCancellationRequested();
            if (record.NeedsVerification)
            {
                var identity = await usage.IdentifyAsync(record.Tokens.AccessToken, ct);
                if (identity.Subject != account.Subject) throw GitHubDeviceFlow.Reconnect();
                record = record with { NeedsVerification = false };
                await secrets.SaveAsync(id, Encode(record));
            }
            ct.ThrowIfCancellationRequested();
            return record.Tokens.AccessToken;
        }
        finally { gate.Release(); }
    }

    async Task PersistReplacementAsync(string id, Envelope record)
    {
        try { await secrets.SaveAsync(id, Encode(record)); pending.TryRemove(id, out _); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new ProviderAccessException("Windows could not save the renewed GitHub sign-in. Retry refresh before closing Radar.", SourceHealth.Stale);
        }
    }

    public async Task RemoveAsync(string id, CancellationToken ct)
    {
        var gate = Gate(id); await gate.WaitAsync(ct);
        try { secrets.Remove(id); pending.TryRemove(id, out _); }
        finally { gate.Release(); }
    }

    static string Encode(Envelope record) => Prefix + JsonSerializer.Serialize(record);
    static void Validate(Envelope record)
    {
        if (record.Version != 1 || string.IsNullOrWhiteSpace(record.Subject) || record.Tokens is not { } tokens ||
            !GitHubDeviceFlow.ValidClientId(tokens.ClientId) || !GitHubDeviceFlow.ValidSecret(tokens.AccessToken) || !GitHubDeviceFlow.ValidSecret(tokens.RefreshToken) ||
            tokens.ExpiresAt == default || tokens.RefreshExpiresAt == default)
            throw GitHubDeviceFlow.Reconnect();
    }
}
