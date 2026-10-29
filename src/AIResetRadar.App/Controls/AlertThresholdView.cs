using AIResetRadar.Core;
using Microsoft.Maui.Handlers;

namespace AIResetRadar.App.Controls;

public sealed class AlertThresholdView : View
{
    public AlertThresholds Thresholds { get; internal set; }
    internal string Section { get; }
    internal Func<AlertThresholds, Task<bool>> Save { get; }
    internal Action<bool> Interaction { get; }
    public AlertThresholdView(decimal warning, decimal critical, decimal info, bool alertsEnabled, string section,
        Func<AlertThresholds, Task<bool>> save, Action<bool> interaction)
    {
        Thresholds = new(warning, critical, info); Section = section; Save = save; Interaction = interaction;
        HeightRequest = 48; IsVisible = alertsEnabled;
    }
    public void SetAlertsEnabled(bool enabled)
    {
        if (!enabled && Handler?.PlatformView is Platforms.Windows.ThresholdEditor editor) editor.Cancel();
        IsVisible = enabled;
    }
    public void Refresh(decimal warning, decimal critical, decimal info, bool enabled)
    {
        SetAlertsEnabled(enabled);
        var thresholds = new AlertThresholds(warning, critical, info);
        if (Handler?.PlatformView is Platforms.Windows.ThresholdEditor editor) editor.Refresh(thresholds);
        else Thresholds = thresholds;
    }
}

public sealed class AlertThresholdViewHandler : ViewHandler<AlertThresholdView, Platforms.Windows.ThresholdEditor>
{
    public static readonly IPropertyMapper<AlertThresholdView, AlertThresholdViewHandler> Mapper = new PropertyMapper<AlertThresholdView, AlertThresholdViewHandler>(ViewMapper);
    public AlertThresholdViewHandler() : base(Mapper) { }
    protected override Platforms.Windows.ThresholdEditor CreatePlatformView() => new(VirtualView);
    protected override void DisconnectHandler(Platforms.Windows.ThresholdEditor platformView)
    {
        platformView.Dispose(); base.DisconnectHandler(platformView);
    }
}
