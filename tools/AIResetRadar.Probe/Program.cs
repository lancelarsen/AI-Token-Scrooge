using AIResetRadar.Infrastructure;
using AIResetRadar.Core;

if (args.Length == 2 && args[0] is "--prepare-demo-isolation" or "--verify-demo-isolation")
{
    var folder = Path.GetFullPath(args[1]);
    var fixtureRoot = Path.GetFullPath(Path.Combine("artifacts", "native-qa")) + Path.DirectorySeparatorChar;
    if (!folder.StartsWith(fixtureRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Use a disposable directory under artifacts/native-qa.");
    var path = Path.Combine(folder, "radar.db");
    if (args[0] == "--prepare-demo-isolation" && File.Exists(path)) throw new InvalidOperationException("The fixture database already exists. Choose a new directory.");
    if (args[0] == "--verify-demo-isolation" && !File.Exists(path)) throw new FileNotFoundException("Fixture database is missing.", path);
    var store = new LocalStore(path);
    if (args[0] == "--prepare-demo-isolation")
    {
        var saved = new ProviderAccount("saved-fixture", "GitHub Copilot", "Saved fixture", "qa-user", "personal", Enabled: false, CredentialId: "11111111111111111111111111111111");
        var snapshot = new QuotaSnapshot(saved.Id, saved.Subject, saved.Scope, DateTimeOffset.UtcNow, [], null);
        await store.WriteMonitoringStateAsync([saved], [snapshot], [saved with { CredentialId = "22222222222222222222222222222222" }]);
        await store.RecordSnapshotAsync(snapshot);
        await store.WriteAsync("settings", new RadarSettings(LowThreshold: 44));
        await store.WriteAsync("alerts", new[] { new AlertEvent("saved-event", saved.Id, null, "Saved fixture event", "Fixture", DateTimeOffset.UtcNow) });
        await store.WriteAsync("snoozed", new Dictionary<string, DateTimeOffset> { [saved.Id] = DateTimeOffset.UtcNow.AddHours(1) });
        Console.WriteLine("Disposable saved-account, pending-retirement and history fixtures prepared. No credentials were created.");
    }
    else
    {
        var saved = (await store.ReadAsync<ProviderAccount[]>("accounts"))?.Single();
        var retired = (await store.ReadAsync<ProviderAccount[]>("retired"))?.Single();
        var snapshot = (await store.ReadAsync<QuotaSnapshot[]>("snapshots"))?.Single();
        var settings = await store.ReadAsync<RadarSettings>("settings");
        var alerts = await store.ReadAsync<AlertEvent[]>("alerts");
        var snoozed = await store.ReadAsync<Dictionary<string, DateTimeOffset>>("snoozed");
        if (saved is not { Id: "saved-fixture", Nickname: "Saved fixture", Enabled: false, CredentialId: "11111111111111111111111111111111" }
            || retired?.CredentialId != "22222222222222222222222222222222" || snapshot?.AccountId != saved.Id || settings?.LowThreshold != 44
            || (await store.HistoryAsync(saved.Id)).Count != 1 || alerts?.Single().Key != "saved-event"
            || snoozed?.Single().Key != saved.Id)
            throw new InvalidDataException("Demo changed saved fixture state or history.");
        Console.WriteLine("Demo isolation passed: saved account, snapshot, pending retirement, settings and history preserved.");
    }
    return 0;
}

if (args.Length == 2 && args[0] == "--inspect-store")
{
    var store = new LocalStore(Path.GetFullPath(args[1]));
    var alerts = await store.ReadAsync<AlertEvent[]>("alerts") ?? [];
    Console.WriteLine($"Alert events: {alerts.Length}; Windows-delivered: {alerts.Count(a => a.Delivered)}");
    return 0;
}

var executable = CodexLocator.Find() ?? throw new InvalidOperationException("Codex executable not found.");
var root = Path.GetFullPath(Path.Combine("artifacts", "isolated-profile-probe"));
// Two unique empty profiles must not inherit an existing signed-in session.
foreach (var name in new[] { "profile-a", "profile-b" })
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(50));
    await using var connection = await CodexConnection.StartAsync(executable, Path.Combine(root, name), timeout.Token);
    var result = await connection.CallAsync("account/read", new { refreshToken = false }, timeout.Token);
    var signedIn = result.TryGetProperty("account", out var account) && account.ValueKind != System.Text.Json.JsonValueKind.Null;
    Console.WriteLine($"{name}: isolated account is {(signedIn ? "SIGNED IN — isolation failed" : "empty as expected")}");
    if (signedIn) return 1;
    try { await connection.CallAsync("thread/start"); throw new Exception("Inference allowlist failed"); }
    catch (InvalidOperationException) { Console.WriteLine("Inference methods rejected by transport."); }
}
return 0;
