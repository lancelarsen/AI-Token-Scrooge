using AIResetRadar.Infrastructure;
namespace AIResetRadar.App.Services;

public sealed class AppServices
{
    public static AppServices Current { get; } = new();
    public string DirectoryPath { get; }
    public LocalStore Store { get; }
    public AccountMonitor Monitor { get; }
    public GitHubDeviceFlow GitHubSignIn { get; }
    public string? GitHubClientId { get; }
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(35) };
    AppServices()
    {
        DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIResetRadar");
        var args = Environment.GetCommandLineArgs(); var dataIndex = Array.IndexOf(args, "--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < args.Length) DirectoryPath = Path.GetFullPath(args[dataIndex + 1]);
        Store = new(Path.Combine(DirectoryPath, "radar.db"));
        GitHubSignIn = new(http, TimeProvider.System);
        GitHubClientId = ReadGitHubClientId();
        Monitor = new(DirectoryPath, new WindowsSecretStore(Path.Combine(DirectoryPath, "Credentials")), new(http, TimeProvider.System), TimeProvider.System, GitHubSignIn);
    }
    static string? ReadGitHubClientId()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "github-app.json");
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return null;
            using var data = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (data.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object || !data.RootElement.TryGetProperty("clientId", out var value) || value.ValueKind != System.Text.Json.JsonValueKind.String) return null;
            var id = value.GetString(); return GitHubDeviceFlow.ValidClientId(id) ? id : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { return null; }
    }
}
