using System.Text.Json;
using AIResetRadar.Core;
namespace AIResetRadar.Infrastructure;

public sealed class AccountMonitor(string directory, ISecretStore secrets, GitHubUsageClient github, TimeProvider clock, GitHubDeviceFlow? githubSignIn = null, ICredentialLifecycleJournal? journal = null, string? securityDirectory = null)
{
    readonly CliProfilePaths profiles = new(directory, securityDirectory);
    readonly GitHubCredentialManager githubCredentials = new(secrets, github, githubSignIn, clock);
    public ClaudeClient Claude { get; } = new(directory, clock, journal, securityDirectory);
    public string ProfilePath(string id, CredentialProfileLocation location = CredentialProfileLocation.LegacyDataDirectory) => profiles.PathFor("OpenAI", id, location);
    public async Task<CodexConnection> OpenCodexAsync(string id, CancellationToken ct = default, CredentialProfileLocation location = CredentialProfileLocation.LegacyDataDirectory) => await CodexConnection.StartAsync(
        CodexLocator.Find() ?? throw new FileNotFoundException("Install the Codex desktop app to connect an OpenAI account."), ProfilePath(id, location), ct);

    public async Task<CodexConnection> BeginCodexSignInAsync(string id, CancellationToken ct = default)
    {
        _ = ProfilePath(id, CredentialProfileLocation.LocalSecurityDirectory);
        if (journal is not null) await journal.StageCredentialAsync("OpenAI", id, ct, CredentialProfileLocation.LocalSecurityDirectory);
        return await OpenCodexAsync(id, ct, CredentialProfileLocation.LocalSecurityDirectory);
    }

    public async Task<(ProviderAccount Account, QuotaSnapshot Snapshot)> CompleteCodexAsync(CodexConnection connection, string id, string nickname, CancellationToken ct, string? credentialId = null)
    {
        var identity = await connection.CallAsync("account/read", new { refreshToken = false }, ct);
        var email = identity.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object ? ProviderMapping.Text(account, "email") : null;
        if (email is null) throw new ProviderAccessException("Complete sign-in with a ChatGPT account in your browser, then try again.", SourceHealth.AuthenticationRequired);
        var data = await connection.CallAsync("account/rateLimits/read", null, ct);
        var provider = new ProviderAccount(id, "OpenAI", nickname, email, ProviderMapping.Text(data, "accountId") ?? "unresolved", CredentialId: credentialId ?? id, ProfileLocation: CredentialProfileLocation.LocalSecurityDirectory);
        return (provider, ProviderMapping.Codex(provider, data, clock.GetUtcNow()) with { PlanType = ProviderMapping.Text(account, "planType") });
    }

    public Task<(ProviderAccount Account, QuotaSnapshot Snapshot)> ConnectGitHubAsync(string nickname, string token, decimal? allowance, UsageUnit unit, string? organization, CancellationToken ct, ProviderAccount? reconnect = null) =>
        ConnectGitHubCoreAsync(nickname, token, null, allowance, unit, organization, ct, reconnect);

    public Task<(ProviderAccount Account, QuotaSnapshot Snapshot)> ConnectGitHubWithBrowserAsync(string nickname, GitHubAppTokens tokens, decimal? allowance, UsageUnit unit, CancellationToken ct, ProviderAccount? reconnect = null)
    {
        if (reconnect is not null && reconnect.Scope != "personal") throw new InvalidDataException("Organization billing uses the advanced token connection.");
        return ConnectGitHubCoreAsync(nickname, tokens.AccessToken, tokens, allowance, unit, null, ct, reconnect);
    }

    async Task<(ProviderAccount Account, QuotaSnapshot Snapshot)> ConnectGitHubCoreAsync(string nickname, string token, GitHubAppTokens? appTokens, decimal? allowance, UsageUnit unit, string? organization, CancellationToken ct, ProviderAccount? reconnect)
    {
        var identity = await github.IdentifyAsync(token, ct);
        if (reconnect is not null && reconnect.Subject != identity.Subject) throw new ProviderAccessException("Sign in to the same GitHub account to reconnect. Add a separate connection for a different account.", SourceHealth.AuthenticationRequired);
        var account = new ProviderAccount(reconnect?.Id ?? Guid.NewGuid().ToString("N"), "GitHub Copilot", nickname, identity.Subject,
            string.IsNullOrWhiteSpace(organization) ? "personal" : "organization:" + organization.Trim().ToLowerInvariant(), Allowance: allowance, Unit: unit, Login: identity.Login, CredentialId: Guid.NewGuid().ToString("N"));
        if (reconnect is not null) account = ReconnectPolicy.Accept(reconnect, account);
        QuotaSnapshot snapshot;
        try { snapshot = await github.ReadAsync(account, token, ct); }
        catch (ProviderAccessException error) { snapshot = new(account.Id, account.Subject, account.Scope, clock.GetUtcNow(), [], null, error.Health, error.Message); }
        ct.ThrowIfCancellationRequested();
        if (journal is not null) await journal.StageCredentialAsync(account.Provider, account.CredentialId!, ct);
        try
        {
            if (appTokens is null) await secrets.SaveAsync(account.CredentialId!, token);
            else await githubCredentials.SaveAppAsync(account.CredentialId!, account.Subject, appTokens);
            ct.ThrowIfCancellationRequested();
        }
        catch
        {
            try { await DisconnectAsync(account); } catch { /* Durable journal retains failed cleanup. */ }
            throw;
        }
        return (account, snapshot);
    }

    public async Task<QuotaSnapshot> RefreshAsync(ProviderAccount account, CancellationToken ct)
    {
        if (account.Provider == "Claude") return await Claude.RefreshAsync(account, ct);
        if (account.Provider == "GitHub Copilot")
        {
            var token = await githubCredentials.ReadAccessTokenAsync(account, ct);
            return await github.ReadAsync(account, token, ct);
        }
        if (account.Provider != "OpenAI") throw new InvalidDataException("Unknown credential provider.");
        await using var connection = await OpenCodexAsync(account.CredentialId ?? account.Id, ct, account.ProfileLocation);
        var identity = await connection.CallAsync("account/read", new { refreshToken = false }, ct);
        if (!identity.TryGetProperty("account", out var data) || data.ValueKind != JsonValueKind.Object || ProviderMapping.Text(data, "email") != account.Subject)
            throw new ProviderAccessException("Codex is signed out or the profile identity changed. Reconnect this account.", SourceHealth.AuthenticationRequired);
        return ProviderMapping.Codex(account, await connection.CallAsync("account/rateLimits/read", null, ct), clock.GetUtcNow()) with { PlanType = ProviderMapping.Text(data, "planType") };
    }
    public async Task DisconnectAsync(ProviderAccount account, CancellationToken ct = default)
    {
        if (account.Provider == "Claude") { await Claude.DisconnectAsync(account, ct); return; }
        if (account.Provider == "GitHub Copilot") await githubCredentials.RemoveAsync(account.CredentialId ?? account.Id, ct);
        else if (account.Provider == "OpenAI")
        {
            await using (var connection = await OpenCodexAsync(account.CredentialId ?? account.Id, ct, account.ProfileLocation))
                await connection.CallAsync("account/logout", ct: ct);
            ct.ThrowIfCancellationRequested();
            await profiles.DeleteAsync(account.Provider, account.CredentialId ?? account.Id, account.ProfileLocation, ct);
        }
        else throw new InvalidDataException("Unknown credential provider.");
        // Removal has completed. A caller deadline must not turn successful deletion
        // into another logout/recreation cycle. Persistence failures still remain retryable.
        if (journal is not null) await journal.CompleteCredentialCleanupAsync(account.CredentialId ?? account.Id);
    }
}
