using Microsoft.Maui.Handlers;

namespace AIResetRadar.App.Controls;

public sealed class MinuteSlider(int initialValue) : View
{
    public int Value { get; private set; } = Math.Clamp(initialValue, 1, 30);
    public event EventHandler? ValueChanged;
    internal void SetValue(int value)
    {
        value = Math.Clamp(value, 1, 30);
        if (Value == value) return;
        Value = value;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class MinuteSliderHandler : ViewHandler<MinuteSlider, Platforms.Windows.MinuteEditor>
{
    public static readonly IPropertyMapper<MinuteSlider, MinuteSliderHandler> Mapper =
        new PropertyMapper<MinuteSlider, MinuteSliderHandler>(ViewMapper);
    public MinuteSliderHandler() : base(Mapper) { }
    protected override Platforms.Windows.MinuteEditor CreatePlatformView() => new(VirtualView);
}
