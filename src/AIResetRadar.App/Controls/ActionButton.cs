namespace AIResetRadar.App.Controls;

// Shared visual action button; text changes also update the action icon.
public sealed class ActionButton : Button
{
    public ActionButton()
    {
        BackgroundColor = Color.FromArgb("#232429"); TextColor = Color.FromArgb("#F2F1F5");
        BorderColor = Color.FromArgb("#45464D"); BorderWidth = 1; CornerRadius = 10;
        Padding = new Thickness(16, 10); FontSize = 14; HeightRequest = 44;
        ContentLayout = new ButtonContentLayout(ButtonContentLayout.ImagePosition.Left, 9);
        Loaded += (_, _) => UpdateNativeContent();
    }
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == TextProperty.PropertyName && Handler is not null) Dispatcher.Dispatch(UpdateNativeContent);
    }
    void UpdateNativeContent()
    {
        if (Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.Button native) return;
        var panel = new Microsoft.UI.Xaml.Controls.Grid { ColumnSpacing = 9 };
        panel.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = Microsoft.UI.Xaml.GridLength.Auto });
        panel.ColumnDefinitions.Add(new Microsoft.UI.Xaml.Controls.ColumnDefinition { Width = new Microsoft.UI.Xaml.GridLength(1, Microsoft.UI.Xaml.GridUnitType.Star) });
        panel.Children.Add(new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = GlyphFor(Text),
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 20,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 181, 160, 244)),
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center });
        var label = new Microsoft.UI.Xaml.Controls.TextBlock { Text = Text ?? "", FontSize = FontSize,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(label, 1);
        panel.Children.Add(label);
        native.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        native.Content = panel;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(native, SemanticProperties.GetDescription(this) ?? Text ?? "");
    }
    public static string GlyphFor(string? text)
    {
        var value = (text ?? "").ToLowerInvariant();
        if (value == "custom order") return "\uE80A";
        if (value == "most remaining") return "\uE9D9";
        if (value == "least remaining") return "\uE9D2";
        if (value == "next reset") return "\uE823";
        if (value.Contains("rename")) return "\uE70F";
        if (value.StartsWith("save")) return "\uE74E";
        if (value.Contains("remove")) return "\uE74D";
        if (value.Contains("copy")) return "\uE8C8";
        if (value.Contains("close") || value.Contains("cancel") || value.Contains("dismiss")) return "\uE711";
        if (value.Contains("snooze")) return "\uE823";
        if (value.Contains("refresh") || value.Contains("default")) return "\uE72C";
        if (value.Contains("settings") || value.Contains("manage account") || value.Contains("rules")) return "\uE713";
        if (value.Contains("history")) return "\uE81C";
        if (value.Contains("notification")) return "\uE7F4";
        if (value.Contains("help")) return "\uE897";
        if (value.Contains("quick view") || value.Contains("dashboard")) return "\uE80F";
        if (value.Contains("token")) return "\uE8D7";
        if (value.Contains("sign in") || value.Contains("connect") || value is "openai codex" or "github copilot" or "claude") return "\uE77B";
        if (value.Contains("add")) return "\uE710";
        if (value.Contains("reset")) return "\uE8B7";
        if (value.Contains("usage")) return "\uE9D9";
        return "\uE8A7";
    }
}
