using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

public sealed record DevinReading(string Subject, decimal? Used, decimal? Limit, DateTimeOffset? Reset);

public interface IDevinBrowser
{
    Task<DevinReading> ReadAsync(Uri origin, string profile, bool interactive, CancellationToken ct);
}

public sealed class DevinClient(string directory, string? securityDirectory, ISecretStore secrets, IDevinBrowser? browser,
    ICredentialLifecycleJournal? journal, TimeProvider clock)
{
    readonly CliProfilePaths profiles = new(directory, securityDirectory);
    const CredentialProfileLocation Location = CredentialProfileLocation.LocalSecurityDirectory;

    public static Uri Workspace(string input)
    {
        var value = input.Trim();
        if (!value.Contains("://", StringComparison.Ordinal)) value = "https://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 || !System.Text.RegularExpressions.Regex.IsMatch(
                uri.IdnHost, @"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.devinenterprise\.com$"))
            throw new InvalidDataException("Enter your company's Devin Enterprise workspace URL.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    public async Task<(ProviderAccount Account, QuotaSnapshot Snapshot)> ConnectAsync(string id, string credential,
        string nickname, string workspace, CancellationToken ct)
    {
        var origin = Workspace(workspace);
        var profile = profiles.PathFor("Devin", credential, Location);
        if (journal is not null) await journal.StageCredentialAsync("Devin", credential, ct, Location);
        using var lease = await secrets.AcquireOperationAsync(credential, ct);
        var reading = await ReadAsync(origin, profile, true, ct);
        var account = new ProviderAccount(id, "Devin", nickname, reading.Subject, origin.Host,
            Unit: UsageUnit.Acus, CredentialId: credential, PollingMinutes: 10, ProfileLocation: Location);
        return (account, Snapshot(account, reading));
    }

    public async Task<QuotaSnapshot> RefreshAsync(ProviderAccount account, CancellationToken ct)
    {
        if (account.ProfileLocation != Location || account.CredentialId is null)
            throw new InvalidDataException("Reconnect this Devin account.");
        using var lease = await secrets.AcquireOperationAsync(account.CredentialId, ct);
        var profile = profiles.PathFor("Devin", account.CredentialId, Location);
        if (!Directory.Exists(profile))
            throw new ProviderAccessException("Sign in to Devin again.", SourceHealth.AuthenticationRequired);
        var reading = await ReadAsync(Workspace(account.Scope), profile, false, ct);
        if (!string.Equals(account.Subject, reading.Subject, StringComparison.Ordinal))
            throw new ProviderAccessException("The Devin user changed. Sign in again with the original account.", SourceHealth.AuthenticationRequired);
        return Snapshot(account, reading);
    }

    async Task<DevinReading> ReadAsync(Uri origin, string profile, bool interactive, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || browser is null) throw new PlatformNotSupportedException();
        WindowsCredentialDirectory.Ensure(profile);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(interactive ? TimeSpan.FromMinutes(10) : TimeSpan.FromSeconds(45));
        return await browser.ReadAsync(origin, profile, interactive, deadline.Token);
    }

    QuotaSnapshot Snapshot(ProviderAccount account, DevinReading reading) => new(account.Id, account.Subject, account.Scope,
        clock.GetUtcNow(), [new("devin/monthly", "Monthly ACUs", UsageUnit.Acus, reading.Used, reading.Limit, reading.Reset,
            AllowanceSource: reading.Limit.HasValue ? AllowanceProvenance.Provider : AllowanceProvenance.Unknown)],
        null, SourceHealth.Healthy, PlanType: "enterprise");

    public async Task DisconnectAsync(ProviderAccount account, CancellationToken ct)
    {
        if (account.ProfileLocation != Location || account.CredentialId is null)
            throw new InvalidDataException("Invalid Devin profile location.");
        using var lease = await secrets.AcquireOperationAsync(account.CredentialId, ct);
        await profiles.DeleteAsync("Devin", account.CredentialId, Location, ct);
        if (journal is not null) await journal.CompleteCredentialCleanupAsync(account.CredentialId);
    }
}
