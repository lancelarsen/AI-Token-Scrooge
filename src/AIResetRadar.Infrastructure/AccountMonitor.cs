using System.Text.Json;
using AIResetRadar.Core;
namespace AIResetRadar.Infrastructure;

public sealed class AccountMonitor(string directory, ISecretStore secrets, GitHubUsageClient github, TimeProvider clock)
{
    public string ProfilePath(string id) => Guid.TryParse(id, out var guid) ? Path.Combine(directory, "Profiles", guid.ToString("N")) : throw new ArgumentException("Invalid account identity.");
    public async Task<CodexConnection> OpenCodexAsync(string id, CancellationToken ct = default) => await CodexConnection.StartAsync(
        CodexLocator.Find() ?? throw new FileNotFoundException("Install the Codex desktop app to connect an OpenAI account."), ProfilePath(id), ct);

    public async Task<(ProviderAccount Account, QuotaSnapshot Snapshot)> CompleteCodexAsync(CodexConnection connection, string id, string nickname, CancellationToken ct, string? credentialId = null)
    {
        var identity = await connection.CallAsync("account/read", new { refreshToken = false }, ct);
        var email = identity.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object ? ProviderMapping.Text(account, "email") : null;
        if (email is null) throw new ProviderAccessException("Complete sign-in with a ChatGPT account in your browser, then try again.", SourceHealth.AuthenticationRequired);
        var data = await connection.CallAsync("account/rateLimits/read", null, ct);
        var provider = new ProviderAccount(id, "OpenAI", nickname, email, ProviderMapping.Text(data, "accountId") ?? "unresolved", CredentialId: credentialId ?? id);
        return (provider, ProviderMapping.Codex(provider, data, clock.GetUtcNow()));
    }

    public async Task<(ProviderAccount Account, QuotaSnapshot Snapshot)> ConnectGitHubAsync(string nickname, string token, decimal? allowance, UsageUnit unit, string? organization, CancellationToken ct, ProviderAccount? reconnect = null)
    {
        var identity = await github.IdentifyAsync(token, ct);
        if (reconnect is not null && reconnect.Subject != identity.Subject) throw new ProviderAccessException("Sign in to the same GitHub account to reconnect. Add a separate connection for a different account.", SourceHealth.AuthenticationRequired);
        var account = new ProviderAccount(reconnect?.Id ?? Guid.NewGuid().ToString("N"), "GitHub Copilot", nickname, identity.Subject,
            string.IsNullOrWhiteSpace(organization) ? "personal" : "organization:" + organization.Trim().ToLowerInvariant(), Allowance: allowance, Unit: unit, Login: identity.Login, CredentialId: Guid.NewGuid().ToString("N"));
        if (reconnect is not null) account = ReconnectPolicy.Accept(reconnect, account);
        QuotaSnapshot snapshot;
        try { snapshot = await github.ReadAsync(account, token, ct); }
        catch (ProviderAccessException error) { snapshot = new(account.Id, account.Subject, account.Scope, clock.GetUtcNow(), [], null, error.Health, error.Message); }
        await secrets.SaveAsync(account.CredentialId!, token); return (account, snapshot);
    }

    public async Task<QuotaSnapshot> RefreshAsync(ProviderAccount account, CancellationToken ct)
    {
        if (account.Provider == "GitHub Copilot")
        {
            var token = await secrets.ReadAsync(account.CredentialId ?? account.Id) ?? throw new ProviderAccessException("Reconnect GitHub to restore monitoring.", SourceHealth.AuthenticationRequired);
            return await github.ReadAsync(account, token, ct);
        }
        await using var connection = await OpenCodexAsync(account.CredentialId ?? account.Id, ct);
        var identity = await connection.CallAsync("account/read", new { refreshToken = false }, ct);
        if (!identity.TryGetProperty("account", out var data) || data.ValueKind != JsonValueKind.Object || ProviderMapping.Text(data, "email") != account.Subject)
            throw new ProviderAccessException("Codex is signed out or the profile identity changed. Reconnect this account.", SourceHealth.AuthenticationRequired);
        return ProviderMapping.Codex(account, await connection.CallAsync("account/rateLimits/read", null, ct), clock.GetUtcNow());
    }
    public async Task DisconnectAsync(ProviderAccount account, CancellationToken ct = default)
    {
        if (account.Provider == "GitHub Copilot") secrets.Remove(account.CredentialId ?? account.Id);
        else
        {
            await using var connection = await OpenCodexAsync(account.CredentialId ?? account.Id, ct);
            await connection.CallAsync("account/logout", ct: ct);
        }
    }
}
