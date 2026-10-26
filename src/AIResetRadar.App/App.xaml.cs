namespace AIResetRadar.App;
public partial class App : Application
{
    public App() { InitializeComponent(); UserAppTheme = AppTheme.Dark; }
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new MainPage();
        var window = new Window(page) { Title = "AI Token Scrooge", Width = 1440, Height = 1024, MinimumWidth = 800, MinimumHeight = 650 };
        if (Environment.GetCommandLineArgs().Contains("--compact")) { window.Width = 800; window.Height = 700; }
        window.HandlerChanged += (_, _) =>
        {
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
            {
                native.AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "app.ico"));
                native.AppWindow.TitleBar.PreferredTheme = Microsoft.UI.Windowing.TitleBarTheme.Dark;
                var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(native.AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
                var width = Math.Min(native.AppWindow.Size.Width, area.Width - 40);
                var height = Math.Min(native.AppWindow.Size.Height, area.Height - 40);
                native.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(area.X + (area.Width - width) / 2, area.Y + 20, width, height));
                page.AttachWindow(window, native);
            }
        };
        return window;
    }
}
