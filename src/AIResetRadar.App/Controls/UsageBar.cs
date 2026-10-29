namespace AIResetRadar.App.Controls;

public sealed class UsageBar : ContentView
{
    readonly Border track;
    readonly Grid content;
    readonly Grid labels;
    public UsageBar(double fill, string used, string left, bool warning, bool critical = false)
    {
        HeightRequest = 48;
        content = new Grid { BackgroundColor = Color.FromArgb("#30333E") };
        track = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 11 },
            HorizontalOptions = LayoutOptions.Start };
        SetWarning(warning, critical);
        content.Children.Add(track);
        content.SizeChanged += (_, _) => track.WidthRequest = Math.Max(0, content.Width * fill);
        labels = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], Padding = new Thickness(15, 0), InputTransparent = true };
        var usedLabel = new Label { Text = used, FontSize = 16, FontFamily = "Segoe UI Semibold", VerticalOptions = LayoutOptions.Center };
        var leftLabel = new Label { Text = left, FontSize = 16, HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center };
        labels.Add(usedLabel); labels.Add(leftLabel, 1);
        content.Children.Add(labels);
        Content = new Border { Stroke = Color.FromArgb("#515461"), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 11 }, Content = content };
        SemanticProperties.SetDescription(this, $"{used}, {left}");
    }
    public void SetWarning(bool warning, bool critical = false) => track.Background = new LinearGradientBrush([
        new GradientStop(Color.FromArgb(critical ? "#B52D42" : warning ? "#B76C27" : "#7560CE"), 0),
        new GradientStop(Color.FromArgb(critical ? "#F05B69" : warning ? "#DF9B46" : "#A990ED"), 1)], new Point(0, 0), new Point(1, 0));
    public void AddAlertEditor(Button details, AlertThresholdView editor)
    {
        // The interactive children provide their own names instead of a single read-only summary.
        ClearValue(SemanticProperties.DescriptionProperty);
        // Paint the fixed labels above the lines without blocking their drag targets.
        details.ZIndex = 1; editor.ZIndex = 2; labels.ZIndex = 3;
        content.Add(details); content.Add(editor);
    }
}
