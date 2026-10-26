using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;
using Native = Microsoft.UI.Xaml;

namespace AIResetRadar.App.Services;

internal static class ThemedDialogs
{
    static readonly SemaphoreSlim queue = new(1, 1);
    static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(string hex) => new(Windows.UI.Color.FromArgb(255,
        Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16)));

    public static async Task<bool> ShowAsync(Microsoft.Maui.Controls.Page page, string title, string message, string accept, string? cancel = null)
    {
        await queue.WaitAsync();
        try
        {
            var root = (page.Handler?.PlatformView as FrameworkElement)?.XamlRoot
                ?? throw new InvalidOperationException("The dialog requires an attached page.");
            var body = new StackPanel { Spacing = 8 };
            foreach (var line in message.Replace("\r", "").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) { body.Children.Add(new Native.Controls.Border { Height = 6 }); continue; }
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14,
                    Foreground = Brush("#BEC1D2"), IsTextSelectionEnabled = true };
                var colon = line.IndexOf(':');
                if (colon > 0 && colon < 45 && !char.IsDigit(line[colon - 1]))
                {
                    text.Inlines.Add(new Run { Text = line[..(colon + 1)] + " ", FontWeight = new Windows.UI.Text.FontWeight { Weight = 600 }, Foreground = Brush("#F2F1F5") });
                    text.Inlines.Add(new Run { Text = line[(colon + 1)..].TrimStart() });
                }
                else text.Text = line;
                body.Children.Add(text);
            }
            var dialog = new ContentDialog { XamlRoot = root, RequestedTheme = ElementTheme.Dark,
                Title = title, CornerRadius = new Native.CornerRadius(16),
                Content = new Native.Controls.ScrollViewer { Content = body, MaxHeight = 420, VerticalScrollBarVisibility = Native.Controls.ScrollBarVisibility.Auto },
                CloseButtonText = cancel ?? accept, PrimaryButtonText = cancel is null ? "" : accept,
                DefaultButton = ContentDialogButton.None };
            dialog.PrimaryButtonStyle = ActionButtonStyle();
            dialog.CloseButtonStyle = ActionButtonStyle();
            return await ShowStyledAsync(dialog) == ContentDialogResult.Primary;
        }
        finally { queue.Release(); }
    }
    static Native.Style ActionButtonStyle()
    {
        var primary = new Native.Style(typeof(Native.Controls.Button));
        primary.Setters.Add(new Native.Setter(Native.Controls.Control.BackgroundProperty, Brush("#232429")));
        primary.Setters.Add(new Native.Setter(Native.Controls.Control.ForegroundProperty, Brush("#FFFFFF")));
        primary.Setters.Add(new Native.Setter(Native.Controls.Control.CornerRadiusProperty, new Native.CornerRadius(10)));
        primary.Setters.Add(new Native.Setter(Native.Controls.Control.FontWeightProperty, new Windows.UI.Text.FontWeight { Weight = 400 }));
        primary.Setters.Add(new Native.Setter(Native.Controls.Control.BorderBrushProperty, Brush("#45464D")));
        primary.Setters.Add(new Native.Setter(Native.Controls.Control.BorderThicknessProperty, new Native.Thickness(1)));
        primary.Setters.Add(new Native.Setter(Native.Controls.Control.PaddingProperty, new Native.Thickness(16, 10, 16, 10)));
        primary.Setters.Add(new Native.Setter(Native.FrameworkElement.MinHeightProperty, 44d));
        return primary;
    }

    static ContentDialog Create(Microsoft.Maui.Controls.Page page, string title, object content, string close) => new()
    {
        XamlRoot = (page.Handler?.PlatformView as FrameworkElement)?.XamlRoot
            ?? throw new InvalidOperationException("The dialog requires an attached page."),
        RequestedTheme = ElementTheme.Dark, CornerRadius = new Native.CornerRadius(16),
        Title = title, Content = content, CloseButtonText = close, DefaultButton = ContentDialogButton.None,
        PrimaryButtonStyle = ActionButtonStyle(), CloseButtonStyle = ActionButtonStyle()
    };

    public static async Task<string?> ChooseAsync(Microsoft.Maui.Controls.Page page, string title, params string[] choices)
    {
        await queue.WaitAsync();
        try
        {
            string? selected = null;
            var content = new StackPanel { Spacing = 10 };
            var dialog = Create(page, title, content, "Cancel");
            foreach (var choice in choices)
            {
                var button = new Native.Controls.Button { Content = choice, HorizontalAlignment = Native.HorizontalAlignment.Stretch, Style = ActionButtonStyle() };
                button.Click += (_, _) => { selected = choice; dialog.Hide(); };
                content.Children.Add(button);
            }
            await ShowStyledAsync(dialog);
            return selected;
        }
        finally { queue.Release(); }
    }

    public static async Task<string?> PromptAsync(Microsoft.Maui.Controls.Page page, string title, string message, string initialValue)
    {
        await queue.WaitAsync();
        try
        {
            var input = new Native.Controls.TextBox { Text = initialValue, CornerRadius = new Native.CornerRadius(10),
                Background = Brush("#2A2B30"), Foreground = Brush("#F2F1F5"), BorderBrush = Brush("#65577F") };
            Native.Automation.AutomationProperties.SetName(input, title);
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#BEC1D2") });
            content.Children.Add(input);
            var dialog = Create(page, title, content, "Cancel");
            dialog.PrimaryButtonText = "Save";
            dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
            return await ShowStyledAsync(dialog) == ContentDialogResult.Primary ? input.Text : null;
        }
        finally { queue.Release(); }
    }

    static async Task<ContentDialogResult> ShowStyledAsync(ContentDialog dialog)
    {
        dialog.Opened += (_, _) => DecorateActions(dialog);
        return await dialog.ShowAsync();
    }
    static void DecorateActions(Native.DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is Native.Controls.Button button && button.Content is string text && !string.IsNullOrWhiteSpace(text))
            {
                var content = new StackPanel { Orientation = Native.Controls.Orientation.Horizontal, Spacing = 9, VerticalAlignment = Native.VerticalAlignment.Center };
                content.Children.Add(new FontIcon { Glyph = AIResetRadar.App.Controls.ActionButton.GlyphFor(text), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 20, Foreground = Brush("#B5A0F4") });
                content.Children.Add(new TextBlock { Text = text, FontSize = 14, VerticalAlignment = Native.VerticalAlignment.Center });
                Native.Automation.AutomationProperties.SetName(button, text);
                button.Content = content;
            }
            else DecorateActions(child);
        }
    }

}
