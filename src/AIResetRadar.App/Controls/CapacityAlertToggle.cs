namespace AIResetRadar.App.Controls;

// Compact, independent on/off segments matching the dashboard sort control.
public sealed class CapacityAlertToggle : Border
{
    readonly ImageButton alarmsOn;
    readonly ImageButton alarmsOff;
    readonly Func<bool, Task<bool>> save;
    readonly string description;
    bool enabled;
    bool saving;

    public CapacityAlertToggle(bool enabled, string description, Func<bool, Task<bool>> save)
    {
        this.enabled = enabled; this.description = description; this.save = save;
        WidthRequest = 81; HeightRequest = 48; VerticalOptions = LayoutOptions.Center;
        Stroke = Color.FromArgb("#45464D"); StrokeThickness = 1;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 };
        BackgroundColor = Color.FromArgb("#232429");
        var grid = new Grid { ColumnSpacing = 0, ColumnDefinitions = [new(GridLength.Star), new(new GridLength(1)), new(GridLength.Star)] };
        alarmsOn = Segment("alarm_on.png", true);
        alarmsOff = Segment("alarm_off.png", false);
        grid.Add(alarmsOn); grid.Add(new BoxView { Color = Color.FromArgb("#45464D") }, 1); grid.Add(alarmsOff, 2);
        Content = grid; UpdateSelection();
    }

    ImageButton Segment(string image, bool value)
    {
        var button = new ImageButton { Source = image, Aspect = Aspect.AspectFit, Padding = new Thickness(9, 12),
            HeightRequest = 46, MinimumWidthRequest = 0, BorderWidth = 0, CornerRadius = 0 };
        Platforms.Windows.ImmediateToolTip.Attach(button, $"Alarms {(value ? "On" : "Off")} · {description}");
        button.Loaded += (_, _) =>
        {
            if (button.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.Button native) return;
            native.MinWidth = 0;
            native.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(value ? 9 : 0, value ? 0 : 9, value ? 0 : 9, value ? 9 : 0);
        };
        button.Clicked += async (_, _) => await SelectAsync(value);
        return button;
    }

    async Task SelectAsync(bool value)
    {
        if (saving || value == enabled) return;
        var previous = enabled;
        saving = true; enabled = value; UpdateSelection();
        alarmsOn.IsEnabled = alarmsOff.IsEnabled = false;
        try { if (!await save(value)) { enabled = previous; UpdateSelection(); } }
        finally { alarmsOn.IsEnabled = alarmsOff.IsEnabled = true; saving = false; }
    }

    void UpdateSelection()
    {
        alarmsOn.BackgroundColor = Color.FromArgb(enabled ? "#483A64" : "#232429");
        alarmsOff.BackgroundColor = Color.FromArgb(enabled ? "#232429" : "#483A64");
        SemanticProperties.SetDescription(alarmsOn, $"Alarms On{(enabled ? ", Selected" : "")} · {description}");
        SemanticProperties.SetDescription(alarmsOff, $"Alarms Off{(!enabled ? ", Selected" : "")} · {description}");
    }
}
