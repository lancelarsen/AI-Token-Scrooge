using AIResetRadar.App.Presentation;
namespace AIResetRadar.App.Controls;

public sealed class HorizonView : Border
{
    readonly HorizonDay[] days;
    readonly Grid grid = new() { ColumnSpacing = 0 };
    readonly ScrollView viewport = new() { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Always };
    double renderedWidth = double.NaN;
    public HorizonView(IEnumerable<HorizonEvent> source, Action<HorizonEvent> open)
    {
        days = HorizonTimeline.Build(source, DateTimeOffset.Now);
        BackgroundColor = Color.FromArgb("#202229"); StrokeThickness = 0;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 };
        Padding = new Thickness(16, 16); viewport.Content = grid; Content = viewport;
        SizeChanged += (_, _) => Render(open); Render(open);
    }
    void Render(Action<HorizonEvent> open)
    {
        if (Math.Abs(renderedWidth - Width) < 1) return;
        renderedWidth = Width;
        grid.Children.Clear(); grid.ColumnDefinitions.Clear();
        var cellWidth = Math.Max(80, (Math.Max(0, Width) - 32) / days.Length);
        for (var i = 0; i < days.Length; i++)
        {
            var day = days[i];
            grid.ColumnDefinitions.Add(new(new GridLength(cellWidth)));
            var color = Color.FromArgb(day.IsToday ? "#D1BEFF" : "#B5A0F4");
            var column = new VerticalStackLayout { Spacing = 7 };
            column.Children.Add(new Label { Text = day.IsToday ? "Today" : day.Date.ToString("ddd"), FontSize = 14,
                FontFamily = "Segoe UI Semibold", TextColor = color, HorizontalTextAlignment = TextAlignment.Center });
            column.Children.Add(new Label { Text = day.Date.ToString("MMM d"), FontSize = 12, TextColor = Color.FromArgb("#BEC1D2"), HorizontalTextAlignment = TextAlignment.Center });
            var line = new Grid { HeightRequest = 22, ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)] };
            if (i > 0) line.Add(new BoxView { Color = Color.FromArgb("#8170B7"), HeightRequest = 2, VerticalOptions = LayoutOptions.Center });
            if (i < days.Length - 1) line.Add(new BoxView { Color = Color.FromArgb("#8170B7"), HeightRequest = 2, VerticalOptions = LayoutOptions.Center }, 1);
            var marker = new Border { WidthRequest = day.IsToday ? 16 : 10, HeightRequest = day.IsToday ? 16 : 10,
                BackgroundColor = day.IsToday || day.Events.Length > 0 ? color : Color.FromArgb("#202229"),
                Stroke = color, StrokeThickness = 2, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 } };
            line.Add(marker); Grid.SetColumnSpan(marker, 2);
            SemanticProperties.SetDescription(marker, $"{(day.IsToday ? "Today, " : "")}{day.Date:dddd, MMMM d}. {day.Events.Length} scheduled events.");
            column.Children.Add(line);
            foreach (var e in day.Events)
            {
                var accent = Color.FromArgb(e.IsExpiry ? "#EAAA51" : "#B5A0F4");
                var details = $"{e.Title}\n{e.Detail}\n{(e.IsExpiry ? "Earned reset expires" : "Quota refreshes")}: {e.At.ToLocalTime():f}";
                var icon = new Grid { WidthRequest = 62, HeightRequest = 49, HorizontalOptions = LayoutOptions.Center };
                icon.Add(new Border { WidthRequest = 42, HeightRequest = 42, VerticalOptions = LayoutOptions.Start,
                    HorizontalOptions = LayoutOptions.Center, Stroke = accent, StrokeThickness = 2,
                    BackgroundColor = Color.FromArgb(e.IsExpiry ? "#49321D" : "#352A50"),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                    Content = new Image { Source = e.Provider == "GitHub Copilot" ? "github.png" : "openai.png", WidthRequest = 27, HeightRequest = 27 } });
                if (e.PlanBadge is { } badge)
                    icon.Add(new Border { Padding = new Thickness(5, 1), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.End,
                        BackgroundColor = Color.FromArgb("#202229"), Stroke = accent,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                        Content = new Label { Text = badge == "Plan unavailable" ? "?" : badge, FontSize = 10, TextColor = accent, FontFamily = "Segoe UI Semibold" } });
                var content = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(2, 3), Children = {
                    icon, new Label { Text = e.At.ToLocalTime().ToString("h:mm tt"), FontSize = 10, TextColor = accent, HorizontalTextAlignment = TextAlignment.Center }
                }};
                var target = new Grid { Margin = new Thickness(2, 0), MinimumHeightRequest = 74 }; target.Add(content);
                var action = new Button { Text = "", BackgroundColor = Colors.Transparent, BorderWidth = 0, HeightRequest = -1,
                    HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
                action.Clicked += (_, _) => open(e);
                ToolTipProperties.SetText(action, details);
                SemanticProperties.SetDescription(action, $"{details}. Open details.");
                target.Add(action); column.Children.Add(target);
            }
            grid.Add(column, i);
        }
        if (days.All(d => d.Events.Length == 0))
            ((VerticalStackLayout)grid.Children[0]).Children.Add(new Label { Text = "No upcoming events reported", FontSize = 12, TextColor = Color.FromArgb("#BEC1D2"), HorizontalTextAlignment = TextAlignment.Center });
    }
}
