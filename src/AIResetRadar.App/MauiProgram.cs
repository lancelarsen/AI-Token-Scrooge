namespace AIResetRadar.App;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<App>()
        .ConfigureMauiHandlers(handlers => handlers.AddHandler<Controls.AlertThresholdView, Controls.AlertThresholdViewHandler>()).Build();
}
