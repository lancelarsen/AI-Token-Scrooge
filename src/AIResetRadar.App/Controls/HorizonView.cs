using Microsoft.Maui.Layouts;
using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
namespace AIResetRadar.App.Controls;

public sealed class HorizonView : Border
{
    HorizonDay[] days;
    TimeZoneInfo timeZone;
    readonly Action<HorizonEvent> open;
    readonly HistoryTimelineView history;
    readonly Switch historyToggle = new() { OnColor = Color.FromArgb("#8670CC"), ThumbColor = Color.FromArgb("#D1BEFF"), IsToggled = true };
    readonly Label refreshLabel = new();
    readonly Label expiryLabel = new();
    bool updating;
    string futureSignature = "";
    readonly Grid grid = new() { ColumnSpacing = 0 };
    readonly ScrollView viewport = new() { Orientation = ScrollOrientation.Horizontal, HorizontalScrollBarVisibility = ScrollBarVisibility.Never };
    readonly Label hint = new() { Text = "Hover or click for details.", FontSize = 13, TextColor = Color.FromArgb("#BEC1D2"), Margin = new Thickness(0, 8, 0, 8), VerticalOptions = LayoutOptions.Center };
    double renderedWidth = double.NaN;
    readonly GraphicsView navigator = new() { HeightRequest = 22 };
    double contentWidth, dragStart, pointerStart;
    bool dragging;
    public HorizonView(IEnumerable<HorizonEvent> source, Action<HorizonEvent> open, TimeZoneInfo? timeZone = null, Action? settings = null,
        Func<bool, Task<bool>>? toggleHistory = null, Func<string, bool, Task<bool>>? selectAccount = null)
    {
        this.open = open;
        history = new HistoryTimelineView(open, selectAccount ?? ((_, _) => Task.FromResult(true)));
        this.timeZone = timeZone ?? TimeZones.Resolve(null);
        days = HorizonTimeline.Build(source, DateTimeOffset.Now, this.timeZone);
        BackgroundColor = Color.FromArgb("#1C1E24"); StrokeThickness = 1; Stroke = Color.FromArgb("#383B46");
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 };
        Padding = new Thickness(12, 18); viewport.Content = grid;
        var header = new FlexLayout { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap, AlignItems = FlexAlignItems.Center, Margin = new Thickness(12, 0) };
        header.Children.Add(new HorizontalStackLayout { Spacing = 12, Margin = new Thickness(0, 0, 22, 0), Children = {
            new Label { Text = "\uE81C", FontFamily = "Segoe Fluent Icons", FontSize = 30, TextColor = Color.FromArgb("#B5A0F4"), VerticalOptions = LayoutOptions.Center },
            new Label { Text = "Reset Horizon", FontSize = 26, FontFamily = "Segoe UI Semibold", VerticalOptions = LayoutOptions.Center }
        } });
        var refreshCount = days.Sum(day => day.Events.Count(e => !e.IsExpiry));
        var expiryCount = days.Sum(day => day.Events.Count(e => e.IsExpiry));
        refreshLabel.Text = $"Quota Refresh ({refreshCount})"; expiryLabel.Text = $"Reset Expires ({expiryCount})";
        header.Children.Add(Legend(refreshLabel, "#B5A0F4"));
        header.Children.Add(Legend(expiryLabel, "#EAAA51"));
        header.Children.Add(new HorizontalStackLayout { Spacing = 7, Margin = new Thickness(0, 0, 18, 0), Children = {
            historyToggle, new Label { Text = "History", FontSize = 13, VerticalOptions = LayoutOptions.Center } } });
        SemanticProperties.SetDescription(historyToggle, "Show Weekly and Monthly Usage History");
        historyToggle.Toggled += async (_, e) => {
            if (updating) return;
            historyToggle.IsEnabled = false;
            try {
                if (toggleHistory is not null && !await toggleHistory(e.Value)) {
                    updating = true; historyToggle.IsToggled = !e.Value; updating = false;
                }
                SetHistoryVisible(historyToggle.IsToggled);
            } finally { historyToggle.IsEnabled = true; }
        };
        header.Children.Add(hint);
        var headerRow = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 8 };
        headerRow.Add(header);
        var settingsButton = new ActionButton { Text = "Settings", IconOnly = true, WidthRequest = 44, Padding = 8, VerticalOptions = LayoutOptions.Start };
        SemanticProperties.SetDescription(settingsButton, "Earned Reset Alert Settings");
        settingsButton.Clicked += (_, _) => settings?.Invoke();
        headerRow.Add(settingsButton, 1);
        navigator.Drawable = new Overview(this);
        navigator.StartInteraction += (_, e) => {
            if (e.Touches.Length > 0 && navigator.Width > 0) {
                var x = e.Touches[0].X * contentWidth / navigator.Width;
                dragStart = viewport.ScrollX;
                if (x < viewport.ScrollX || x > viewport.ScrollX + viewport.Width)
                {
                    dragStart = Math.Clamp(x - viewport.Width / 2, 0, Math.Max(0, contentWidth - viewport.Width));
                    ScrollTo(dragStart, false);
                }
                pointerStart = e.Touches[0].X;
                dragging = true;
            }
        };
        navigator.DragInteraction += (_, e) => {
            if (dragging && e.Touches.Length > 0 && navigator.Width > 0)
                ScrollTo(dragStart + (e.Touches[0].X - pointerStart) * contentWidth / navigator.Width, false);
        };
        navigator.EndInteraction += (_, _) => dragging = false;
        navigator.CancelInteraction += (_, _) => dragging = false;
        SemanticProperties.SetDescription(navigator, "Timeline overview. Drag the highlighted window or click the track to move through dates.");
        viewport.Scrolled += (_, _) => UpdateNavigator();
        Content = new VerticalStackLayout { Spacing = 16, Children = {
            headerRow, new BoxView { HeightRequest = 1, Color = Color.FromArgb("#383B46"), Margin = new Thickness(12, 0) }, viewport,
            navigator, history
        } };
        viewport.SizeChanged += (_, _) => Render(open);
        SizeChanged += (_, _) => Render(open); Render(open);
        SetHistoryVisible(true);
    }
    public void Update(IEnumerable<HorizonEvent> source, HistorySeries[] series, HistoryPreferences choices, TimeZoneInfo zone,
        DateTimeOffset now, bool loading = false, string? error = null)
    {
        var events = source.ToArray(); timeZone = zone;
        var signature = zone.Id + now.Date.ToString("O") + string.Join("|", events.Select(e => e.ToString()));
        if (futureSignature != signature) {
            futureSignature = signature; days = HorizonTimeline.Build(events, now, zone); renderedWidth = double.NaN;
            refreshLabel.Text = $"Quota Refresh ({events.Count(e => !e.IsExpiry)})";
            expiryLabel.Text = $"Reset Expires ({events.Count(e => e.IsExpiry)})";
            if (!choices.Enabled) Render(open);
        }
        updating = true; historyToggle.IsToggled = choices.Enabled; updating = false;
        SetHistoryVisible(choices.Enabled);
        history.Update(series, events, choices, zone, now, loading, error);
        hint.Text = choices.Enabled ? "Scroll Left for History" : "Hover or Click for Details";
    }
    void SetHistoryVisible(bool enabled)
    {
        if (history.IsVisible && !enabled) history.FollowToday();
        history.IsVisible = enabled; viewport.IsVisible = navigator.IsVisible = !enabled;
        if (!enabled) { renderedWidth = double.NaN; Render(open); }
    }
    static View Legend(Label label, string color)
    {
        label.FontSize = 13; label.TextColor = Color.FromArgb("#BEC1D2"); label.VerticalOptions = LayoutOptions.Center;
        return new HorizontalStackLayout { Spacing = 7, Margin = new Thickness(0, 8, 20, 8), VerticalOptions = LayoutOptions.Center, Children = {
        new Border { WidthRequest = 10, HeightRequest = 10, StrokeThickness = 0, BackgroundColor = Color.FromArgb(color), VerticalOptions = LayoutOptions.Center,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 } },
        label
    } };
    }
    void Render(Action<HorizonEvent> open)
    {
        // Use the actual viewport so border thickness and pixel rounding cannot create a tiny overflow.
        var available = Math.Max(0, (viewport.Width > 0 ? viewport.Width : Width - 26) - 1);
        if (Math.Abs(renderedWidth - available) < 1) return;
        renderedWidth = available;
        grid.Children.Clear(); grid.ColumnDefinitions.Clear();
        var minimumWidth = 80 * days.Length;
        hint.Text = minimumWidth > available ? "Scroll for more days · Hover or click for details." : "Hover or click for details.";
        var cellWidth = Math.Max(80, available / Math.Max(1, days.Length));
        contentWidth = Math.Max(available, minimumWidth);
        for (var i = 0; i < days.Length; i++)
        {
            var day = days[i];
            grid.ColumnDefinitions.Add(new(new GridLength(cellWidth)));
            var color = Color.FromArgb(day.IsToday ? "#D1BEFF" : "#B5A0F4");
            var column = new VerticalStackLayout { Spacing = 7 };
            column.Children.Add(new TimelineDayHeading(day.Date, day.IsToday));
            // Fill every day cell, including its outer halves, to match the history rail.
            var line = new Grid { HeightRequest = 22 };
            line.Add(new BoxView { Color = Color.FromArgb("#8170B7"), HeightRequest = 2, VerticalOptions = LayoutOptions.Center });
            var marker = new Border { WidthRequest = day.IsToday ? 16 : 10, HeightRequest = day.IsToday ? 16 : 10,
                BackgroundColor = day.IsToday || day.Events.Length > 0 ? color : Color.FromArgb("#1C1E24"),
                Stroke = color, StrokeThickness = 2, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 } };
            line.Add(marker);
            SemanticProperties.SetDescription(marker, $"{(day.IsToday ? "Today, " : "")}{day.Date:dddd, MMMM d}. {day.Events.Length} scheduled events.");
            column.Children.Add(line);
            foreach (var e in day.Events)
            {
                var accent = Color.FromArgb(e.IsExpiry ? "#EAAA51" : "#B5A0F4");
                var details = $"{e.DisplayTitle}\n{e.Detail}\n{(e.IsExpiry ? "Reset Expires" : "Quota Refresh")}: {TimeZones.Format(e.At, timeZone)}";
                var icon = new Grid { WidthRequest = 62, HeightRequest = 49, HorizontalOptions = LayoutOptions.Center };
                icon.Add(new Border { WidthRequest = 42, HeightRequest = 42, VerticalOptions = LayoutOptions.Start,
                    HorizontalOptions = LayoutOptions.Center, Stroke = accent, StrokeThickness = 2,
                    BackgroundColor = Color.FromArgb(e.IsExpiry ? "#49321D" : "#352A50"),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                    Content = new Image { Source = e.Provider == "Claude" ? "claude.png" : e.Provider == "GitHub Copilot" ? "github.png" : "openai.png", WidthRequest = 27, HeightRequest = 27 } });
                if (e.PlanBadge is { } badge)
                    icon.Add(new Border { Padding = new Thickness(5, 1), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.End,
                        BackgroundColor = Color.FromArgb("#1C1E24"), Stroke = accent,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                        Content = new Label { Text = badge == "Plan unavailable" ? "?" : badge, FontSize = 10, TextColor = accent, FontFamily = "Segoe UI Semibold" } });
                var content = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(2, 3), Children = {
                    icon, new Label { Text = TimeZones.Format(e.At, timeZone, "h:mm tt"), FontSize = 10, TextColor = accent, HorizontalTextAlignment = TextAlignment.Center }
                }};
                var target = new Grid { Margin = new Thickness(2, 0), MinimumHeightRequest = 74 }; target.Add(content);
                var action = new Button { Text = "", BackgroundColor = Colors.Transparent, BorderWidth = 0, HeightRequest = -1,
                    HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
                action.Clicked += (_, _) => open(e);
                Platforms.Windows.ImmediateToolTip.Attach(action, details);
                SemanticProperties.SetDescription(action, $"{details}. Open details.");
                target.Add(action); column.Children.Add(target);
            }
            grid.Add(column, i);
        }
        UpdateNavigator();
        if (days.All(d => d.Events.Length == 0))
            ((VerticalStackLayout)grid.Children[0]).Children.Add(new Label { Text = "No upcoming events reported", FontSize = 12, TextColor = Color.FromArgb("#BEC1D2"), HorizontalTextAlignment = TextAlignment.Center });
    }
    void ScrollTo(double x, bool animate) => _ = viewport.ScrollToAsync(Math.Clamp(x, 0, Math.Max(0, contentWidth - viewport.Width)), 0, animate);
    void UpdateNavigator() => navigator.Invalidate();
    sealed class Overview(HorizonView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            var width = rect.Width - 2;
            canvas.FillColor = Color.FromArgb("#232429");
            canvas.FillRoundedRectangle(1, 1, width, 20, 5);
            canvas.StrokeColor = Color.FromArgb("#45464D");
            canvas.StrokeSize = 1;
            canvas.DrawRoundedRectangle(1, 1, width, 20, 5);
            var total = Math.Max(1, owner.contentWidth);
            var fraction = Math.Clamp(owner.viewport.Width / total, 0, 1);
            var left = (float)(Math.Clamp(owner.viewport.ScrollX / total, 0, 1 - fraction) * width) + 1;
            var thumbWidth = (float)(fraction * width);
            canvas.FillColor = Color.FromArgb("#483A64");
            canvas.FillRoundedRectangle(left, 1, thumbWidth, 20, 5);
            canvas.StrokeColor = Color.FromArgb("#B5A0F4");
            canvas.DrawRoundedRectangle(left, 1, thumbWidth, 20, 5);
            // Project the same equally spaced column centers as the timeline.
            // At full range this is pixel-aligned with the dates and event icons above.
            double columnLeft = 0;
            var scale = Math.Min(1, rect.Width / total);
            for (var i = 0; i < owner.days.Length; i++)
            {
                var columnWidth = owner.grid.ColumnDefinitions[i].Width.Value;
                var x = (float)((columnLeft + columnWidth / 2) * scale);
                columnLeft += columnWidth;
                var kinds = owner.days[i].Events.Select(e => e.IsExpiry).Distinct().ToArray();
                for (var j = 0; j < kinds.Length; j++)
                {
                    canvas.FillColor = Color.FromArgb(kinds[j] ? "#EAAA51" : "#B5A0F4");
                    // Circular event marks stay centered on the matching day column.
                    canvas.FillCircle(x, kinds.Length > 1 ? 9 + j * 5 : 11, kinds.Length > 1 ? 2.2f : 3);
                }
            }
            if (fraction < .999) {
                canvas.FillColor = Color.FromArgb("#D1BEFF");
                for (var x = -1; x <= 1; x++)
                    for (var y = 0; y < 2; y++) canvas.FillCircle(left + thumbWidth / 2 + x * 5, 9 + y * 4, 1.2f);
            }
        }
    }
}
