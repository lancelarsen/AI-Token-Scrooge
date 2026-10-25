namespace AIResetRadar.App.WinUI;
public partial class App : MauiWinUIApplication
{
    readonly Mutex instance;
    public App()
    {
        instance = new Mutex(true, "Local\\AIResetRadar.Desktop", out var created);
        if (!created)
        {
            var hwnd = FindWindow(null, "AI Reset Radar");
            if (hwnd != 0) { ShowWindow(hwnd, 9); SetForegroundWindow(hwnd); }
            Environment.Exit(0);
        }
        _ = AIResetRadar.App.Platforms.Windows.NotificationService.Current;
        UnhandledException += (_, e) => {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIResetRadar");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "last-crash.log"), e.Exception.GetType().Name + "\n" + e.Exception.StackTrace);
        };
        InitializeComponent();
    }
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern nint FindWindow(string? className, string title);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(nint hwnd, int command);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hwnd);
}
