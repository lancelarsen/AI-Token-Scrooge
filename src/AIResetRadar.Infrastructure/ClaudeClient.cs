using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

public sealed class ClaudeClient(string directory, TimeProvider clock)
{
    public string ProfilePath(string id) => Guid.TryParse(id, out var guid) ? Path.Combine(directory, "ClaudeProfiles", guid.ToString("N")) : throw new ArgumentException("Invalid account identity.");
    public static string Find() => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe"))
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe")
        : throw new FileNotFoundException("Install Claude Code to connect a Claude account.");
    internal static ProcessStartInfo StartInfo(string executable, string profile)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = profile, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("ANTHROPIC_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("CLAUDE_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
        start.Environment["CLAUDE_CONFIG_DIR"] = profile;
        return start;
    }
    async Task<string> RunAsync(string profile, CancellationToken ct, params string[] args)
    {
        Directory.CreateDirectory(profile);
        var start = StartInfo(Find(), profile);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Claude Code could not start.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(ct); var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var text = await stdout; await stderr;
            if (process.ExitCode != 0) throw new ProviderAccessException("Claude sign-in could not be confirmed. Reconnect this account.", SourceHealth.AuthenticationRequired);
            return text;
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
    }
    public async Task<(ProviderAccount Account, QuotaSnapshot Snapshot)> ConnectAsync(string id, string credentialId, string nickname, CancellationToken ct)
    {
        var profile = ProfilePath(credentialId);
        await RunAsync(profile, ct, "auth", "login");
        var (subject, scope, plan) = await IdentityAsync(profile, ct);
        var account = new ProviderAccount(id, "Claude", nickname, subject, scope, Login: subject, CredentialId: credentialId);
        var snapshot = await ReadUsageAsync(account, profile, plan, ct);
        return (account, snapshot);
    }
    async Task<(string Subject, string Scope, string Plan)> IdentityAsync(string profile, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(await RunAsync(profile, ct, "auth", "status"));
        var root = document.RootElement;
        var email = ProviderMapping.Text(root, "email"); var org = ProviderMapping.Text(root, "orgId");
        if (!root.TryGetProperty("loggedIn", out var logged) || logged.ValueKind != JsonValueKind.True || ProviderMapping.Text(root, "authMethod") != "claude.ai" || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(org))
            throw new ProviderAccessException("Sign in with your Claude subscription account.", SourceHealth.AuthenticationRequired);
        return (email.ToLowerInvariant(), org, ProviderMapping.Text(root, "subscriptionType") ?? "unknown");
    }
    public async Task<QuotaSnapshot> RefreshAsync(ProviderAccount account, CancellationToken ct)
    {
        var profile = ProfilePath(account.CredentialId ?? account.Id);
        var (subject, scope, plan) = await IdentityAsync(profile, ct);
        if (subject != account.Subject || scope != account.Scope) throw new ProviderAccessException("Claude profile identity changed. Reconnect this account.", SourceHealth.AuthenticationRequired);
        return await ReadUsageAsync(account, profile, plan, ct);
    }
    public async Task DisconnectAsync(ProviderAccount account, CancellationToken ct) => await RunAsync(ProfilePath(account.CredentialId ?? account.Id), ct, "auth", "logout");
    async Task<QuotaSnapshot> ReadUsageAsync(ProviderAccount account, string profile, string plan, CancellationToken ct)
    {
        // Older clients require an interactive terminal. Never fall back to model inference.
        var version = await RunAsync(profile, ct, "--version");
        if (!Version.TryParse(version.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var parsed) || parsed < new Version(2, 1, 310))
            throw new ProviderAccessException("Update Claude Code to version 2.1.310 or later to read usage in Scrooge.", SourceHealth.Unsupported);
        var text = await RunAsync(profile, ct, "/usage");
        var windows = ClaudeUsageParser.Parse(text, clock.GetUtcNow());
        if (windows.Count == 0) throw new ProviderAccessException("Claude Code did not return readable usage limits. Check Claude's Usage page.", SourceHealth.Unsupported);
        return new(account.Id, account.Subject, account.Scope, clock.GetUtcNow(), windows, null,
            Message: "Read from Claude Code. Reset times are shown to the nearest minute; reset credits are not supplied by this source.", PlanType: plan);
    }
}

public static class ClaudeUsageParser
{
    public static IReadOnlyList<QuotaWindow> Parse(string screen, DateTimeOffset now)
    {
        var result = new List<QuotaWindow>();
        foreach (var item in new[] { ("Current session", "session", "Current session", 300), ("Current week (all models)", "weekly", "Weekly limit", 10080) })
        {
            var start = screen.IndexOf(item.Item1, StringComparison.Ordinal);
            if (start < 0) continue;
            var section = screen[(start + item.Item1.Length)..];
            var end = Regex.Match(section, @"\r?\n\s*(?:Current |Extra usage|Esc to cancel)");
            if (end.Success) section = section[..end.Index];
            var match = Regex.Match(section, @"(?<![\w.+-])(?<used>\d+(?:\.\d+)?)% used");
            if (!match.Success || !decimal.TryParse(match.Groups["used"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var used) || used is < 0 or > 100) continue;
            var reset = Regex.Match(section, @"Resets (?<when>[^\r\n(]+)\s*\((?<zone>[^)]+)\)", RegexOptions.IgnoreCase);
            result.Add(new(item.Item2, item.Item3, UsageUnit.Percent, used, 100, reset.Success ? ParseReset(reset.Groups["when"].Value.Trim(), reset.Groups["zone"].Value, now, item.Item4) : null, item.Item4, AllowanceProvenance.Provider));
        }
        return result;
    }
    static DateTimeOffset? ParseReset(string value, string zoneId, DateTimeOffset now, int duration)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            var localNow = TimeZoneInfo.ConvertTime(now, zone).DateTime;
            var formats = new[] { "h:mmtt", "htt", "MMM d, h:mmtt", "MMM d, htt" };
            if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)) return null;
            var hasDate = value.Contains(',');
            var date = hasDate ? new DateTime(localNow.Year, parsed.Month, parsed.Day).Add(parsed.TimeOfDay) : localNow.Date.Add(parsed.TimeOfDay);
            if (!hasDate && date < localNow.AddMinutes(-1)) date = date.AddDays(1);
            if (hasDate && date < localNow.AddDays(-1)) date = date.AddYears(1);
            if (zone.IsInvalidTime(date) || zone.IsAmbiguousTime(date)) return null;
            var candidate = new DateTimeOffset(date, zone.GetUtcOffset(date));
            return candidate >= now.AddMinutes(-1) && candidate <= now.AddMinutes(duration + 1) ? candidate : null;
        }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return null; }
    }
}
