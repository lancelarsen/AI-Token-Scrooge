using AIResetRadar.Infrastructure;
namespace AIResetRadar.App.Services;

public sealed class AppServices
{
    public static AppServices Current { get; } = new();
    public string DirectoryPath { get; }
    public LocalStore Store { get; }
    public AccountMonitor Monitor { get; }
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(35) };
    AppServices()
    {
        DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIResetRadar");
        var args = Environment.GetCommandLineArgs(); var dataIndex = Array.IndexOf(args, "--data-dir");
        if (dataIndex >= 0 && dataIndex + 1 < args.Length) DirectoryPath = Path.GetFullPath(args[dataIndex + 1]);
        Store = new(Path.Combine(DirectoryPath, "radar.db"));
        Monitor = new(DirectoryPath, new WindowsSecretStore(Path.Combine(DirectoryPath, "Credentials")), new(http, TimeProvider.System), TimeProvider.System);
    }
}
