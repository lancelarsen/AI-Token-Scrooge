using AIResetRadar.App.Presentation;
namespace AIResetRadar.App.Controls;

public sealed class HorizonView : Border
{
    readonly HorizonEvent[] events;
    readonly Grid grid = new() { ColumnSpacing = 20, RowSpacing = 8 };
    readonly ScrollView viewport = new() { Orientation = ScrollOrientation.Horizontal, HeightRequest = 132, HorizontalScrollBarVisibility = ScrollBarVisibility.Always };
    public HorizonView(IEnumerable<HorizonEvent> source, Action<string> open)
    {
        events = source.ToArray(); BackgroundColor = Color.FromArgb("#202229"); StrokeThickness = 0;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 };
        Padding = new Thickness(24, 18); viewport.Content = grid; Content = viewport;
        SizeChanged += (_, _) => Render(open); Render(open);
    }
    void Render(Action<string> open)
    {
        grid.Children.Clear(); grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        if (events.Length == 0) { grid.Children.Add(new Label { Text = "Upcoming resets and earned-credit expirations will appear here.", TextColor = Color.FromArgb("#B6B8C8") }); return; }
        var columns = events.Length;
        var cellWidth = Math.Max(190, (Width - 48 - 20 * (columns - 1)) / columns);
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new(new GridLength(cellWidth)));
        grid.RowDefinitions.Add(new(GridLength.Auto));
        for (var i = 0; i < events.Length; i++)
        {
            var e = events[i]; var color = Color.FromArgb(e.IsExpiry ? "#EAAA51" : "#B5A0F4");
            var line = new Grid { RowDefinitions = [new(new GridLength(22))] };
            line.Children.Add(new BoxView { Color = Color.FromArgb("#8170B7"), HeightRequest = 2, VerticalOptions = LayoutOptions.Center });
            line.Children.Add(new Label { Text = e.IsExpiry ? "\uE823" : "\uEA3A", FontFamily = "Segoe Fluent Icons", FontSize = 20, TextColor = color, BackgroundColor = Color.FromArgb("#202229"), HorizontalOptions = LayoutOptions.Start, Padding = new Thickness(0, 0, 5, 0) });
            var content = new VerticalStackLayout { Spacing = 5, Padding = new Thickness(0, 2), Children = {
                new Label { Text = e.At.ToLocalTime().Date == DateTime.Today ? "Today" : e.At.ToLocalTime().ToString("ddd, MMM d"), FontSize = 15, FontFamily = "Segoe UI Semibold", TextColor = color }, line,
                new Label { Text = e.At.ToLocalTime().ToString("h:mm tt"), FontSize = 14, FontFamily = "Segoe UI Semibold", TextColor = color },
                new Label { Text = e.Title, FontSize = 13 }, new Label { Text = e.Detail, FontSize = 12, TextColor = Color.FromArgb("#BEC1D2") }
            }};
            var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => open(e.AccountId); content.GestureRecognizers.Add(tap);
            SemanticProperties.SetDescription(content, $"{e.Title}. {e.Detail}. {e.At.ToLocalTime():f}");
            grid.Add(content, i % columns, i / columns);
        }
    }
}
