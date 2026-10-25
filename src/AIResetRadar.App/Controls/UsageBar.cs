namespace AIResetRadar.App.Controls;

public sealed class UsageBar : ContentView
{
    public UsageBar(double fill, string used, string left, bool warning)
    {
        HeightRequest = 48;
        var content = new Grid { BackgroundColor = Color.FromArgb("#30333E") };
        var track = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 11 },
            Background = new LinearGradientBrush([new GradientStop(Color.FromArgb(warning ? "#B76C27" : "#7560CE"), 0),
                new GradientStop(Color.FromArgb(warning ? "#DF9B46" : "#A990ED"), 1)], new Point(0, 0), new Point(1, 0)), HorizontalOptions = LayoutOptions.Start };
        content.Children.Add(track);
        content.SizeChanged += (_, _) => track.WidthRequest = Math.Max(0, content.Width * fill);
        var labels = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], Padding = new Thickness(15, 0) };
        var usedLabel = new Label { Text = used, FontSize = 16, FontFamily = "Segoe UI Semibold", VerticalOptions = LayoutOptions.Center };
        var leftLabel = new Label { Text = left, FontSize = 16, HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center };
        labels.Add(usedLabel); labels.Add(leftLabel, 1);
        content.Children.Add(labels);
        Content = new Border { Stroke = Color.FromArgb("#515461"), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 11 }, Content = content };
        SemanticProperties.SetDescription(this, $"{used}, {left}");
    }
}
