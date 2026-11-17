using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

public interface IOpenAiBillingBrowser
{
    Task<OpenAiBillingReading> ReadAsync(string profile, bool interactive, string? expectedSubject, string? expectedOrganization, CancellationToken ct);
}

public sealed class OpenAiBillingClient(string directory, string? securityDirectory, ISecretStore secrets,
    IOpenAiBillingBrowser? browser, ICredentialLifecycleJournal? journal, TimeProvider clock)
{
    readonly CliProfilePaths profiles = new(directory, securityDirectory);
    const CredentialProfileLocation Location = CredentialProfileLocation.LocalSecurityDirectory;

    public async Task<(ProviderAccount Account, QuotaSnapshot Snapshot)> ConnectAsync(string id, string credential,
        string nickname, CancellationToken ct)
    {
        var profile = profiles.PathFor(OpenAiBilling.Provider, credential, Location);
        if (journal is not null) await journal.StageCredentialAsync(OpenAiBilling.Provider, credential, ct, Location);
        using var lease = await secrets.AcquireOperationAsync(credential, ct);
        var reading = await ReadAsync(profile, true, null, null, ct);
        var account = new ProviderAccount(id, OpenAiBilling.Provider, nickname, reading.Subject, reading.Organization,
            Unit: UsageUnit.Usd, CredentialId: credential, PollingMinutes: 10, ProfileLocation: Location);
        return (account, OpenAiBilling.Snapshot(account, reading, clock.GetUtcNow()));
    }

    public async Task<QuotaSnapshot> RefreshAsync(ProviderAccount account, CancellationToken ct)
    {
        Validate(account);
        using var lease = await secrets.AcquireOperationAsync(account.CredentialId!, ct);
        var profile = profiles.PathFor(OpenAiBilling.Provider, account.CredentialId!, Location);
        if (!Directory.Exists(profile)) throw new ProviderAccessException("Sign in to OpenAI Platform again.", SourceHealth.AuthenticationRequired);
        var reading = await ReadAsync(profile, false, account.Subject, account.Scope, ct);
        return OpenAiBilling.Snapshot(account, reading, clock.GetUtcNow());
    }

    async Task<OpenAiBillingReading> ReadAsync(string profile, bool interactive, string? subject, string? organization, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || browser is null) throw new PlatformNotSupportedException();
        WindowsCredentialDirectory.Ensure(profile);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(interactive ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(40));
        return await browser.ReadAsync(profile, interactive, subject, organization, deadline.Token);
    }

    public async Task DisconnectAsync(ProviderAccount account, CancellationToken ct)
    {
        Validate(account);
        using var lease = await secrets.AcquireOperationAsync(account.CredentialId!, ct);
        await profiles.DeleteAsync(OpenAiBilling.Provider, account.CredentialId!, Location, ct);
        if (journal is not null) await journal.CompleteCredentialCleanupAsync(account.CredentialId!);
    }

    static void Validate(ProviderAccount account)
    {
        if (account.Provider != OpenAiBilling.Provider || account.ProfileLocation != Location || account.CredentialId is null)
            throw new InvalidDataException("Reconnect this OpenAI API account.");
    }
}
