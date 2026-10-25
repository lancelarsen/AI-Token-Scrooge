using AIResetRadar.Infrastructure;

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
