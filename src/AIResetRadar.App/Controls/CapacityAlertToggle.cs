namespace AIResetRadar.App.Controls;

// Independent checkbox behavior with the circular appearance of a radio button.
public sealed class CapacityAlertToggle : CheckBox
{
    public CapacityAlertToggle(bool enabled, string description, Func<bool, Task<bool>> save)
    {
        IsChecked = enabled; WidthRequest = 32; HeightRequest = 44;
        MinimumWidthRequest = 32; VerticalOptions = LayoutOptions.Center;
        Color = Color.FromArgb("#B5A0F4");
        SemanticProperties.SetDescription(this, description);
        Platforms.Windows.ImmediateToolTip.Attach(this, description);
        Loaded += (_, _) =>
        {
            if (Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.CheckBox native) return;
            native.MinWidth = 32; native.MinHeight = 44; native.Padding = new Microsoft.UI.Xaml.Thickness(0);
            native.Template = (Microsoft.UI.Xaml.Controls.ControlTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="CheckBox">
                    <Grid x:Name="LayoutRoot" Width="32" Height="44" Background="Transparent">
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="CommonStates">
                                <VisualState x:Name="Normal" />
                                <VisualState x:Name="PointerOver" />
                                <VisualState x:Name="Pressed" />
                                <VisualState x:Name="Disabled">
                                    <VisualState.Setters><Setter Target="LayoutRoot.Opacity" Value="0.45" /></VisualState.Setters>
                                </VisualState>
                            </VisualStateGroup>
                            <VisualStateGroup x:Name="CheckStates">
                                <VisualState x:Name="Unchecked" />
                                <VisualState x:Name="Indeterminate" />
                                <VisualState x:Name="Checked">
                                    <VisualState.Setters><Setter Target="Dot.Visibility" Value="Visible" /></VisualState.Setters>
                                </VisualState>
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>
                        <Ellipse Width="22" Height="22" Stroke="#B5A0F4" StrokeThickness="2" Fill="#232429" />
                        <Ellipse x:Name="Dot" Width="10" Height="10" Fill="#B5A0F4" Visibility="Collapsed" />
                    </Grid>
                </ControlTemplate>
                """);
        };
        var saving = false;
        CheckedChanged += async (_, args) =>
        {
            if (saving) return;
            saving = true; IsEnabled = false;
            try { if (!await save(args.Value)) IsChecked = !args.Value; }
            finally { IsEnabled = true; saving = false; }
        };
    }
}
