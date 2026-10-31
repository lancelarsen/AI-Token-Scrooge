using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
using Microsoft.Maui.Controls.Shapes;

namespace AIResetRadar.App.Controls;

// One renderer for both horizon modes; history only adds selectors, graph and past events.
public sealed class HistoryTimelineView : Grid
{
    static readonly Color Accent = Color.FromArgb("#B5A0F4");
    static readonly Color Muted = Color.FromArgb("#BEC1D2");
    bool historyEnabled = true;
    float AxisLeft => historyEnabled ? 40 : 0;
    float PlotRight => historyEnabled ? 12 : 0;
    float railY = 60;
    float PlotTop => railY + 32;
    static readonly DateOnly CoordinateAnchor = new(2000, 1, 1);
    float plotBottom = 256;
    readonly VerticalStackLayout selectors = new() { Spacing = 9, Padding = new Thickness(3, 8, 10, 0) };
    readonly ScrollView selectorScroll;
    readonly Grid selectorRail;
    readonly Grid chartHost = new() { HeightRequest = 294, IsClippedToBounds = true, BackgroundColor = Colors.Transparent };
    readonly GraphicsView chart = new() { HeightRequest = 294 };
    readonly Button chartInput = new() { Text = "", BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0,
        HeightRequest = -1, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
    readonly AbsoluteLayout eventsLayer = new() { InputTransparent = true, CascadeInputTransparent = false };
    readonly AbsoluteLayout headingsLayer = new() { InputTransparent = true };
    readonly Dictionary<DateOnly, TimelineDayHeading> dayHeadings = [];
    readonly GraphicsView navigator = new() { HeightRequest = 22 };
    readonly Button navigatorInput = new() { Text = "", BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0,
        HeightRequest = -1, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
    readonly Label status = new() { FontSize = 13, TextColor = Muted, HorizontalTextAlignment = TextAlignment.Center,
        VerticalOptions = LayoutOptions.Center, InputTransparent = true };
    readonly Dictionary<string, View> eventViews = [];
    readonly List<(float X, float Y, HistoryEventKind Kind, int Count)> historicalMarkers = [];
    readonly Dictionary<string, string> colors = [];
    readonly Dictionary<DateTimeOffset, double> sampleCoordinates = [];
    HistorySeries[]? projectedSeries;
    string? projectedZone;
    double projectionOrigin;
    readonly Func<string, bool, Task<bool>> select;
    readonly Action<HorizonEvent> open;
    HistorySeries[] series = [];
    HorizonEvent[] future = [];
    HistoryPreferences preferences = new();
    TimeZoneInfo zone = TimeZoneInfo.Utc;
    DateTimeOffset now = DateTimeOffset.Now;
    HorizonViewport model = new(DateTimeOffset.Now, null, TimeZoneInfo.Utc, 1);
    string selectorSignature = "";
    string? loadError;
    bool loading;
    bool navigating;
    double dragOffset, dragPointer;
    (HistorySeries Series, HistoryPoint Point)? cursor;
    public bool IsNavigating => navigating;

    public HistoryTimelineView(Action<HorizonEvent> open, Func<string, bool, Task<bool>> select)
    {
        this.open = open; this.select = select;
        ColumnDefinitions = [new(new GridLength(112)), new(GridLength.Star)];
        RowDefinitions = [new(GridLength.Auto), new(new GridLength(22))];
        RowSpacing = 12; ColumnSpacing = 10;
        selectorScroll = new ScrollView { Content = selectors, HeightRequest = 294, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        selectorRail = new Grid { ColumnDefinitions = [new(GridLength.Star), new(new GridLength(1))] };
        selectorRail.Add(selectorScroll); selectorRail.Add(new BoxView { Color = Color.FromArgb("#383B46"), Margin = new Thickness(0, 6) }, 1);
        this.Add(selectorRail); chartHost.Add(chart); chartHost.Add(chartInput); chartHost.Add(headingsLayer); chartHost.Add(eventsLayer); chartHost.Add(status); this.Add(chartHost, 1);
        var navigatorHost = new Grid { HeightRequest = 22 };
        navigatorHost.Add(navigator); navigatorHost.Add(navigatorInput);
        this.Add(navigatorHost, 0, 1); Grid.SetColumnSpan((BindableObject)navigatorHost, 2);
        chart.Drawable = new Plot(this); navigator.Drawable = new Overview(this);
        chart.SizeChanged += (_, _) => Reflow();
        SizeChanged += (_, _) => Reflow();
        SemanticProperties.SetDescription(navigator, "History Timeline Navigator · Drag Left for the Past Seven Days · Drag Right for Upcoming Events");
        Platforms.Windows.HistoryChartInput.Attach(chart, Hover, ClearHover, MoveCursor, Pan, FollowToday, chartInput);
        Platforms.Windows.HistoryChartInput.AttachNavigator(navigator, Pan, FollowToday, navigatorInput, BeginNavigation, DragNavigation, () => navigating = false);
    }

    public void SetHistoryEnabled(bool enabled)
    {
        if (historyEnabled == enabled) return;
        historyEnabled = enabled;
        ClearHover(); Reflow();
    }

    void BeginNavigation(double x)
    {
        if (navigator.Width <= 0) return;
        navigating = true;
        var thumb = model.Overview(navigator.Width);
        if (x < thumb.Left || x > thumb.Left + thumb.Width) model.CenterFromOverview(x, navigator.Width);
        dragOffset = model.ScrollOffset; dragPointer = x; cursor = null; Redraw();
    }

    void DragNavigation(double x)
    {
        if (!navigating) return;
        model.DragOverview(dragOffset, x - dragPointer, navigator.Width); Redraw();
    }

    public void Update(HistorySeries[] data, IEnumerable<HorizonEvent> upcoming, HistoryPreferences choices,
        TimeZoneInfo timeZone, DateTimeOffset currentTime, bool isLoading = false, string? error = null)
    {
        series = data; future = upcoming.OrderBy(e => e.At).ToArray(); preferences = choices;
        zone = timeZone; now = currentTime; loading = isLoading; loadError = error;
        // Keep each account's color stable through refresh, selection, and dashboard reordering.
        foreach (var item in series)
            if (!colors.ContainsKey(item.Account.Id)) colors[item.Account.Id] = HistoryChartLayout.ColorForAccount(item.Account.Id);
        BuildSelectors(); Reflow();
    }

    string ColorOf(HistorySeries item) => colors.GetValueOrDefault(item.Account.Id, "#B99AF8");
    IEnumerable<HistorySeries> Visible => series.Where(s => preferences.Includes(s.Account.Id));
    float Span => (float)Math.Max(1, chart.Width - AxisLeft - PlotRight);
    double ContentX(DateTimeOffset at) => sampleCoordinates.TryGetValue(at, out var coordinate)
        ? projectionOrigin + coordinate * model.PixelsPerDay : model.PositionAt(at);
    float PlotX(DateTimeOffset at) => AxisLeft + (float)(ContentX(at) - model.ScrollOffset);
    float PlotY(decimal remaining) => plotBottom - (float)(100 - remaining) / 100 * (plotBottom - PlotTop);
    float TodayX => AxisLeft + (float)(model.TodayPosition - model.ScrollOffset);

    void Reflow()
    {
        if (chart.Width <= 0) return;
        selectorRail.IsVisible = historyEnabled;
        ColumnDefinitions[0].Width = new GridLength(historyEnabled ? (Width < 850 ? 95 : 112) : 0);
        ColumnSpacing = historyEnabled ? 10 : 0;
        var firstRecorded = series.SelectMany(s => s.Points).Select(p => (DateTimeOffset?)p.At).Min() ?? now;
        model.Update(now, future.LastOrDefault()?.At, zone, Span, historyEnabled, earliestHistory: firstRecorded);
        projectionOrigin = model.PositionAt(CoordinateAnchor);
        if (!ReferenceEquals(projectedSeries, series) || projectedZone != zone.Id)
        {
            sampleCoordinates.Clear();
            foreach (var at in series.SelectMany(s => s.Points.Select(p => p.At)).Distinct())
                sampleCoordinates[at] = (model.PositionAt(at) - projectionOrigin) / model.PixelsPerDay;
            projectedSeries = series; projectedZone = zone.Id;
        }
        var futureRows = future.GroupBy(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.At, zone).DateTime))
            .Select(g => g.Count()).DefaultIfEmpty().Max();
        var height = historyEnabled ? HistoryChartLayout.Height(series.Length, futureRows) : railY + 21 + Math.Max(1, futureRows) * 76;
        plotBottom = height - 38;
        if (chart.HeightRequest != height)
            chart.HeightRequest = chartHost.HeightRequest = selectorScroll.HeightRequest = height;
        Redraw();
    }

    // All points remain available for exact hover; only the drawing path is downsampled.
    IEnumerable<HistoryPoint> PointsWithin(HistorySeries item, double left, double right)
    {
        var points = item.Points; var low = 0; var high = points.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (ContentX(points[middle].At) < left) low = middle + 1;
            else high = middle;
        }
        for (var i = low; i < points.Length && ContentX(points[i].At) <= right; i++) yield return points[i];
    }

    void Redraw()
    {
        LayoutDayHeadings(); LayoutEvents(); UpdateStatus(); chart.Invalidate(); navigator.Invalidate();
    }

    void LayoutDayHeadings()
    {
        if (chart.Width <= 0) return;
        HashSet<DateOnly> active = [];
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(model.VisibleStart, zone).DateTime);
        for (var date = first; date <= model.LastDate; date = date.AddDays(1))
        {
            var x = AxisLeft + model.FutureDayPosition(date) - model.ScrollOffset;
            if (x > AxisLeft + Span) break;
            if (x < AxisLeft) continue;
            active.Add(date);
            if (!dayHeadings.TryGetValue(date, out var heading))
            {
                heading = new TimelineDayHeading(date, date == model.Today);
                heading.SizeChanged += (_, _) => AlignDayRail();
                dayHeadings.Add(date, heading); headingsLayer.Add(heading);
            }
            heading.UpdateToday(date == model.Today);
            AbsoluteLayout.SetLayoutBounds(heading, new Rect(x - model.PixelsPerDay / 2, 0, model.PixelsPerDay, AbsoluteLayout.AutoSize));
        }
        foreach (var date in dayHeadings.Keys.Where(date => !active.Contains(date)).ToArray())
        { headingsLayer.Remove(dayHeadings[date]); dayHeadings.Remove(date); }
    }

    void AlignDayRail()
    {
        var headingHeight = dayHeadings.Values.Select(h => h.Height).DefaultIfEmpty().Max();
        if (headingHeight <= 0) return;
        // Same 7px stack gap and 11px rail half-height as the non-history day cell.
        var next = (float)headingHeight + 18;
        if (Math.Abs(railY - next) < .25f) return;
        railY = next;
        Dispatcher.Dispatch(Reflow);
    }

    void BuildSelectors()
    {
        var signature = string.Join("|", series.Select(s => $"{s.Account.Id}:{s.Account.Nickname}:{s.PlanLabel}:{preferences.Includes(s.Account.Id)}:{s.Status}:{CoverageDetails(s)}"));
        if (selectorSignature == signature) return;
        selectorSignature = signature; selectors.Children.Clear();
        foreach (var item in series)
        {
            var enabled = preferences.Includes(item.Account.Id);
            var accent = enabled ? Accent : Color.FromArgb("#666A78");
            var icon = new Grid { WidthRequest = 42, HeightRequest = 44 };
            icon.Add(new Border { WidthRequest = 38, HeightRequest = 38, Stroke = accent, StrokeThickness = 1.5,
                StrokeShape = new RoundRectangle { CornerRadius = 11 }, BackgroundColor = Color.FromArgb(enabled ? "#352A50" : "#232429"),
                Content = new Image { Source = Logo(item.Account.Provider), WidthRequest = 25, HeightRequest = 25, Opacity = enabled ? 1 : .45 } });
            icon.Add(new Border { WidthRequest = 15, HeightRequest = 15, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End,
                StrokeShape = new RoundRectangle { CornerRadius = 4 }, Stroke = accent,
                BackgroundColor = enabled ? Accent : Color.FromArgb("#232429"), Content = new Label { Text = enabled ? "\uE73E" : "",
                    FontFamily = "Segoe Fluent Icons", FontSize = 10, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center } });
            var info = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = {
                new Label { Text = item.Account.Nickname, FontSize = 11, TextColor = enabled ? Colors.White : Muted, LineBreakMode = LineBreakMode.TailTruncation },
                new Border { Padding = new Thickness(5, 1), HorizontalOptions = LayoutOptions.Start, Stroke = accent,
                    StrokeShape = new RoundRectangle { CornerRadius = 5 }, BackgroundColor = Color.FromArgb("#272331"),
                    Content = new Label { Text = item.PlanLabel == "Plan unavailable" ? item.Period : item.PlanLabel, FontSize = 10, TextColor = accent } }
            } };
            var content = new Grid { ColumnDefinitions = [new(new GridLength(44)), new(GridLength.Star)], ColumnSpacing = 5, Padding = new Thickness(2, 4) };
            content.Add(icon); content.Add(info, 1);
            var swatch = new BoxView { HeightRequest = 2, Color = Color.FromArgb(ColorOf(item)), Opacity = enabled ? 1 : .22, Margin = new Thickness(4, 0, 4, 0) };
            var target = new Grid { MinimumHeightRequest = 60, RowDefinitions = [new(GridLength.Star), new(new GridLength(2))] };
            target.Add(content); target.Add(swatch, 0, 1);
            var action = new Button { BackgroundColor = Colors.Transparent, Text = "", BorderWidth = 0, HeightRequest = 60 };
            SemanticProperties.SetDescription(action, $"{(enabled ? "Hide" : "Show")} {item.Account.Provider} / {item.Account.Nickname} · {item.PlanLabel} · {item.Period} History");
            Platforms.Windows.ImmediateToolTip.Attach(action, $"{item.Account.Provider} / {item.Account.Nickname} · {item.PlanLabel}\n{item.Period} History{CoverageDetails(item)}{(item.Status is null ? "" : "\n" + item.Status)}");
            action.Clicked += async (_, _) => {
                action.IsEnabled = false;
                try {
                    if (await select(item.Account.Id, !enabled)) {
                        preferences = preferences.WithAccount(item.Account.Id, !enabled); cursor = null; BuildSelectors(); Redraw();
                    }
                } finally { action.IsEnabled = true; }
            };
            target.Add(action); Grid.SetRowSpan((BindableObject)action, 2); selectors.Children.Add(target);
        }
    }

    static string Logo(string provider) => provider == "Claude" ? "claude.png" : provider == "GitHub Copilot" ? "github.png" : "openai.png";
    void UpdateStatus()
    {
        var inPast = TodayX > AxisLeft + 30;
        status.IsVisible = historyEnabled && inPast && (loadError is not null || loading && series.All(s => s.Points.Length == 0) || !Visible.Any() ||
            !Visible.Any(s => PointsWithin(s, model.ScrollOffset, model.ScrollOffset + Span).Any()));
        if (!historyEnabled && future.Length == 0) {
            status.IsVisible = true; status.Text = "No Upcoming Events Reported"; return;
        }
        status.Text = loadError ?? (loading ? "Loading History" : !Visible.Any() ? "Select an Account to View History" : "No Recorded Usage in This Period");
    }

    void LayoutEvents()
    {
        if (chart.Width <= 0) return;
        HashSet<string> active = [];
        historicalMarkers.Clear();
        var groups = future.GroupBy(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.At, zone).DateTime));
        foreach (var group in groups)
        {
            var dateX = AxisLeft + (float)(model.FutureDayPosition(group.Key) - model.ScrollOffset);
            if (dateX < (historyEnabled ? Math.Max(AxisLeft, TodayX) : AxisLeft) - 24 || dateX > AxisLeft + Span + 12) continue;
            var slot = 0;
            foreach (var e in group)
            {
                var key = $"future:{e.AccountId}:{e.WindowId}:{e.IsExpiry}:{e.At:O}:{slot}:{zone.Id}:{e.DisplayTitle}:{e.Detail}:{e.PlanBadge}";
                active.Add(key);
                if (!eventViews.TryGetValue(key, out var view)) {
                    view = FutureEvent(e); eventViews[key] = view; eventsLayer.Add(view);
                }
                AbsoluteLayout.SetLayoutBounds(view, new Rect(dateX - 31, railY + 21 + slot * 76, 62, 74)); slot++;
            }
        }
        var historical = (historyEnabled ? Visible : []).SelectMany(s => s.Events.Select(e => (Series: s, Event: e)))
            .Where(item => item.Event.At <= now && PlotX(item.Event.At) >= AxisLeft + 10 && PlotX(item.Event.At) <= AxisLeft + Span - 10)
            .OrderBy(item => item.Event.At).ToArray();
        // Keep each event at its actual timestamp, never an average of nearby events.
        var laneEnds = new List<float>();
        foreach (var group in historical.GroupBy(item => (item.Event.At, item.Event.Kind)))
        {
            var values = group.ToArray();
            var x = PlotX(group.Key.At);
            var kinds = values.Select(v => v.Event.Kind).Distinct().Order().ToArray();
            var width = kinds.Length * 22 + (kinds.Length - 1) * 2;
            var lane = laneEnds.FindIndex(right => x - width / 2f >= right + 2);
            if (lane < 0) { lane = laneEnds.Count; laneEnds.Add(0); }
            laneEnds[lane] = x + width / 2f;
            for (var i = 0; i < kinds.Length; i++)
                historicalMarkers.Add((x - width / 2f + 11 + i * 24, railY + lane * 24, kinds[i], kinds.Length == 1 ? values.Length : 1));
            var eventType = group.Key.Kind == HistoryEventKind.ReportedExpiration ? "Reset Expiration" : "Quota Refresh";
            var details = string.Join("\n\n", values.Select(v => $"{v.Series.Account.Provider} / {v.Series.Account.Nickname} · {v.Series.PlanLabel}\n{eventType}: {TimeZones.Format(v.Event.At, zone)}\n{v.Event.Detail}"));
            var key = "history:" + details;
            active.Add(key);
            if (!eventViews.TryGetValue(key, out var view)) {
                view = EventMarker(details);
                eventViews[key] = view; eventsLayer.Add(view);
            }
            AbsoluteLayout.SetLayoutBounds(view, new Rect(x - width / 2f, railY - 11 + lane * 24, width, 22));
        }
        foreach (var key in eventViews.Keys.Where(key => !active.Contains(key)).ToArray()) { eventsLayer.Remove(eventViews[key]); eventViews.Remove(key); }
    }

    View EventMarker(string details)
    {
        // The chart draws the whole circle. This surface only handles interaction, so
        // native Border/Label measurement cannot crop the marker's lower edge.
        var hit = new ContentView { BackgroundColor = Colors.Transparent, Padding = 0,
            HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(hit, details); Platforms.Windows.ImmediateToolTip.Attach(hit, details);
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await Services.ThemedDialogs.ShowAsync(Application.Current!.Windows[0].Page!, "History Event", details, "Close");
        hit.GestureRecognizers.Add(tap);
        return hit;
    }

    View FutureEvent(HorizonEvent e)
    {
        var accent = Color.FromArgb(e.IsExpiry ? "#EAAA51" : "#B5A0F4");
        var icon = new Grid { WidthRequest = 62, HeightRequest = 49 };
        icon.Add(new Border { WidthRequest = 42, HeightRequest = 42, VerticalOptions = LayoutOptions.Start, HorizontalOptions = LayoutOptions.Center,
            Stroke = accent, StrokeThickness = 2, BackgroundColor = Color.FromArgb(e.IsExpiry ? "#49321D" : "#352A50"), StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new Image { Source = Logo(e.Provider ?? "OpenAI"), WidthRequest = 27, HeightRequest = 27 } });
        if (e.PlanBadge is { } badge) icon.Add(new Border { Padding = new Thickness(5, 1), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.End,
            BackgroundColor = Color.FromArgb("#1C1E24"), Stroke = accent, StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Content = new Label { Text = badge == "Plan unavailable" ? "?" : badge, FontSize = 10, FontFamily = "Segoe UI Semibold", TextColor = accent } });
        var target = new Grid(); target.Add(new VerticalStackLayout { Spacing = 3, Children = { icon,
            new Label { Text = TimeZones.Format(e.At, zone, "h:mm tt"), FontSize = 10, TextColor = accent, HorizontalTextAlignment = TextAlignment.Center } } });
        var action = new Button { Text = "", BackgroundColor = Colors.Transparent, BorderWidth = 0, HeightRequest = -1 };
        var details = $"{e.DisplayTitle}\n{e.Detail}\n{(e.IsExpiry ? "Reset Expires" : "Quota Refresh")}: {TimeZones.Format(e.At, zone)}";
        action.Clicked += (_, _) => open(e); SemanticProperties.SetDescription(action, details); Platforms.Windows.ImmediateToolTip.Attach(action, details);
        target.Add(action); return target;
    }

    public string? Hover(double x, double y)
    {
        if (!historyEnabled || navigating || x < AxisLeft || x > AxisLeft + Span || x > TodayX || y < PlotTop || y > plotBottom) { ClearHover(); return null; }
        var contentX = x - AxisLeft + model.ScrollOffset;
        var nearest = Visible.SelectMany(s => PointsWithin(s, contentX - 18, contentX + 18).Select(p => (Series: s, Point: p)))
            .OrderBy(v => Math.Abs(PlotX(v.Point.At) - x) + Math.Abs(PlotY(v.Point.RemainingPercent) - y) * .45).FirstOrDefault();
        if (nearest.Series is null)
        {
            // A gap has no recorded point near the pointer, but its connecting line is inspectable.
            foreach (var item in Visible)
            {
                var points = item.Points; var low = 0; var high = points.Length;
                while (low < high) { var mid = low + (high - low) / 2; if (ContentX(points[mid].At) < contentX) low = mid + 1; else high = mid; }
                if (low == 0 || low >= points.Length) continue;
                var after = points[low]; var before = points[low - 1];
                if (!after.InterpolatedFromPrevious || after.StartsSegment) continue;
                var fraction = (contentX - ContentX(before.At)) / (ContentX(after.At) - ContentX(before.At));
                var remaining = before.RemainingPercent + (after.RemainingPercent - before.RemainingPercent) * (decimal)fraction;
                if (Math.Abs(PlotY(remaining) - y) > 24) continue;
                var at = before.At.AddTicks((long)((after.At - before.At).Ticks * fraction));
                var estimate = new HistoryPoint(at, remaining, after.WindowId, false, before.At, null, true);
                cursor = (item, estimate); chart.Invalidate(); return Details(item, estimate);
            }
            ClearHover(); return null;
        }
        cursor = nearest; chart.Invalidate(); return Details(nearest.Series, nearest.Point);
    }
    string CoverageDetails(HistorySeries s) => s.Points.FirstOrDefault() is { } first && first.At > model.StartAt
        ? $"\nHistory Begins {TimeZones.Format(first.At, zone)}" : "";

    string Details(HistorySeries s, HistoryPoint p) =>
        $"{s.Account.Provider} / {s.Account.Nickname} · {s.PlanLabel}\n{s.Period} · {100 - p.RemainingPercent:0.#}% Used\n{TimeZones.Format(p.At, zone)}" +
        CoverageDetails(s) +
        (p.InterpolatedFromPrevious ? "\nEstimated Linear Connection From Previous Observation" : "") +
        (p.ReplenishedFrom is { } previous ? $"\nCapacity Rose From {previous:0.#}% to {p.RemainingPercent:0.#}%\nObserved Between Polls" +
            (p.PreviousObservedAt is { } prior ? $"\n{TimeZones.Format(prior, zone)} – {TimeZones.Format(p.At, zone)}" : "") : "");
    public void ClearHover() { cursor = null; chart.Invalidate(); }
    public string? MoveCursor(int direction)
    {
        if (!historyEnabled) return null;
        var values = Visible.SelectMany(s => PointsWithin(s, model.ScrollOffset, model.ScrollOffset + Span).Select(p => (Series: s, Point: p)))
            .OrderBy(v => v.Point.At).ToArray();
        if (values.Length == 0) return null;
        var index = cursor is { } current ? Array.FindIndex(values, v => v == current) : direction > 0 ? -1 : values.Length;
        cursor = values[Math.Clamp(index + direction, 0, values.Length - 1)]; chart.Invalidate();
        return Details(cursor.Value.Series, cursor.Value.Point);
    }
    public void Pan(double days) { model.ScrollTo(model.ScrollOffset + days * model.PixelsPerDay); ClearHover(); Redraw(); }
    public void FollowToday() { model.FollowToday(); ClearHover(); Redraw(); }

    sealed class Plot(HistoryTimelineView owner) : IDrawable
    {
        public void Draw(ICanvas c, RectF rect)
        {
            var end = owner.AxisLeft + owner.Span; var today = owner.TodayX; var pastEnd = Math.Clamp(today, owner.AxisLeft, end);
            c.Font = Microsoft.Maui.Graphics.Font.Default; c.FontSize = 11; c.FontColor = Muted;
            if (owner.historyEnabled && pastEnd > owner.AxisLeft + 2) {
                c.StrokeSize = 1; c.StrokeColor = Color.FromArgb("#343741");
                for (var percent = 0; percent <= 100; percent += 25) {
                    var y = owner.PlotY(100 - percent); c.DrawLine(owner.AxisLeft, y, pastEnd, y);
                    c.DrawString($"{percent}%", 18, y - 7, 38, 16, HorizontalAlignment.Right, VerticalAlignment.Center);
                }
                c.SaveState(); c.Translate(7, (owner.PlotTop + owner.plotBottom) / 2); c.Rotate(-90);
                c.FontSize = 10; c.DrawString("% Used", -70, -7, 140, 16, HorizontalAlignment.Center, VerticalAlignment.Center); c.RestoreState();
            }
            c.StrokeColor = Color.FromArgb("#8170B7"); c.StrokeSize = 2;
            c.DrawLine(owner.AxisLeft, owner.railY, end, owner.railY);
            var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(owner.model.VisibleStart, owner.zone).DateTime);
            for (var date = first; date <= owner.model.LastDate.AddDays(7); date = date.AddDays(1)) {
                var x = owner.AxisLeft + (float)(owner.model.PositionAt(date) - owner.model.ScrollOffset);
                if (x > end + owner.model.PixelsPerDay) break;
                if (owner.historyEnabled && x >= owner.AxisLeft && x <= pastEnd) {
                    c.StrokeColor = Color.FromArgb("#30333C"); c.DrawLine(x, owner.PlotTop, x, owner.plotBottom);
                }
                var futureX = owner.AxisLeft + (float)(owner.model.FutureDayPosition(date) - owner.model.ScrollOffset);
                if (futureX < owner.AxisLeft || futureX > end || date > owner.model.LastDate) continue;
                var hasEvents = owner.future.Any(e => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.At, owner.zone).DateTime) == date);
                // Both modes use these same circles: 16px Today, 10px other days.
                // Paint the hollow interior over the rail before adding its inset 2px stroke.
                var isToday = date == owner.model.Today;
                c.FillColor = isToday ? Color.FromArgb("#D1BEFF") : hasEvents ? Accent : Color.FromArgb("#1C1E24");
                c.FillCircle(futureX, owner.railY, isToday ? 8 : 5);
                if (!hasEvents && !isToday) {
                    c.StrokeColor = Accent; c.StrokeSize = 2;
                    c.DrawCircle(futureX, owner.railY, 4);
                }
            }
            if (!owner.historyEnabled) return;
            foreach (var marker in owner.historicalMarkers) {
                var expiry = marker.Kind == HistoryEventKind.ReportedExpiration;
                c.FillColor = Color.FromArgb(expiry ? "#EAAA51" : "#B99AF8");
                c.FillCircle(marker.X, marker.Y, 11);
                c.Font = new Microsoft.Maui.Graphics.Font(marker.Count > 1 ? "Segoe UI Semibold" : "Segoe Fluent Icons");
                c.FontSize = 14; c.FontColor = Color.FromArgb("#232429");
                c.DrawString(marker.Count > 1 ? marker.Count.ToString() : expiry ? "\uE823" : "\uE72C",
                    marker.X - 11, marker.Y - 11, 22, 22, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
            c.Font = Microsoft.Maui.Graphics.Font.Default;
            c.SaveState(); c.ClipRectangle(owner.AxisLeft, owner.PlotTop, owner.Span, owner.plotBottom - owner.PlotTop);
            foreach (var s in owner.Visible) {
                var color = Color.FromArgb(owner.ColorOf(s));
                var points = UsageHistory.Downsample(s.Points, owner.ContentX, owner.model.ScrollOffset, owner.model.ScrollOffset + owner.Span);
                List<PointF> segment = [];
                void Flush() {
                    if (segment.Count == 0) return;
                    var path = new PathF(); path.MoveTo(segment[0]); foreach (var point in segment.Skip(1)) path.LineTo(point);
                    var fill = new PathF(); fill.MoveTo(segment[0].X, owner.plotBottom); foreach (var point in segment) fill.LineTo(point); fill.LineTo(segment[^1].X, owner.plotBottom); fill.Close();
                    c.FillColor = color.WithAlpha(.09f); c.FillPath(fill); c.StrokeColor = color; c.StrokeSize = 2; c.DrawPath(path);
                    if (segment.Count == 1) { c.FillColor = color; c.FillCircle(segment[0], 2.5f); } segment.Clear();
                }
                foreach (var p in points) {
                    if (p.StartsSegment) Flush();
                    var x = owner.PlotX(p.At); var y = owner.PlotY(p.RemainingPercent);
                    if (p.ReplenishedFrom is { } before && segment.Count > 0) segment.Add(new PointF(x, owner.PlotY(before)));
                    segment.Add(new PointF(x, y));
                }
                Flush();
            }
            foreach (var e in owner.Visible.SelectMany(s => s.Events)) {
                var x = owner.PlotX(e.At); if (x < owner.AxisLeft || x > pastEnd) continue;
                c.StrokeColor = Color.FromArgb(e.Kind == HistoryEventKind.ReportedExpiration ? "#EAAA51" : "#B99AF8").WithAlpha(.6f);
                c.StrokeSize = 1; c.StrokeDashPattern = [4, 4]; c.DrawLine(x, owner.PlotTop, x, owner.plotBottom); c.StrokeDashPattern = null;
            }
            if (owner.cursor is { } hit) {
                var x = owner.PlotX(hit.Point.At); var y = owner.PlotY(hit.Point.RemainingPercent);
                c.StrokeColor = Muted.WithAlpha(.6f); c.StrokeSize = 1; c.StrokeDashPattern = [3, 3]; c.DrawLine(x, owner.PlotTop, x, owner.plotBottom); c.StrokeDashPattern = null;
                c.FillColor = Color.FromArgb(owner.ColorOf(hit.Series)); c.FillCircle(x, y, 4); c.StrokeColor = Colors.White; c.DrawCircle(x, y, 4);
            }
            c.RestoreState();
            if (today >= owner.AxisLeft - 1 && today <= end + 1) {
                c.StrokeColor = Accent; c.StrokeSize = 1.5f; c.StrokeDashPattern = [4, 4]; c.DrawLine(today, owner.PlotTop, today, owner.plotBottom); c.StrokeDashPattern = null;
            }
            var labels = owner.Visible.Select(s => (Series: s, Point: owner.PointsWithin(s, owner.model.ScrollOffset, owner.model.ScrollOffset + owner.Span).LastOrDefault()))
                .Where(v => v.Point is not null).OrderBy(v => v.Point!.RemainingPercent).ToArray();
            var labelPositions = HistoryChartLayout.PlaceLabels(labels.Select(v => owner.PlotY(v.Point!.RemainingPercent) - 10).ToArray(), owner.PlotTop, owner.plotBottom - 21);
            for (var i = 0; i < labels.Length; i++) {
                var item = labels[i]; var p = item.Point!; var x = owner.PlotX(p.At); var y = owner.PlotY(p.RemainingPercent);
                var labelY = labelPositions[i];
                var color = Color.FromArgb(owner.ColorOf(item.Series)); c.FillColor = color; c.FillCircle(x, y, 3);
                if (today < end - 80 || x < end - 70) continue;
                var labelX = end - 86;
                c.StrokeColor = color.WithAlpha(.6f); c.StrokeSize = 1; c.DrawLine(x - 4, y, labelX + 80, labelY + 10);
                c.FillColor = Color.FromArgb("#202229"); c.FillRoundedRectangle(labelX, labelY, 80, 21, 6);
                c.StrokeColor = color; c.StrokeSize = 1; c.DrawRoundedRectangle(labelX, labelY, 80, 21, 6);
                c.FontColor = color; c.FontSize = 10;
                c.DrawString($"{item.Series.PlanLabel} {100 - p.RemainingPercent:0.#}%", labelX + 3, labelY, 74, 21, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }

    sealed class Overview(HistoryTimelineView owner) : IDrawable
    {
        public void Draw(ICanvas c, RectF rect)
        {
            var width = Math.Max(1, rect.Width - 2); var thumb = owner.model.Overview(width);
            c.FillColor = Color.FromArgb("#232429"); c.FillRoundedRectangle(1, 1, width, 20, 5);
            c.StrokeColor = Color.FromArgb("#45464D"); c.StrokeSize = 1; c.DrawRoundedRectangle(1, 1, width, 20, 5);
            c.FillColor = Color.FromArgb("#483A64"); c.FillRoundedRectangle((float)thumb.Left + 1, 1, (float)thumb.Width, 20, 5);
            c.StrokeColor = Accent; c.DrawRoundedRectangle((float)thumb.Left + 1, 1, (float)thumb.Width, 20, 5);
            var events = owner.future.Select(e => (e.At, Expiry: e.IsExpiry, Future: true))
                .Concat((owner.historyEnabled ? owner.Visible : []).SelectMany(s => s.Events.Select(e => (e.At, Expiry: e.Kind == HistoryEventKind.ReportedExpiration, Future: false))));
            foreach (var group in events.GroupBy(e => (Day: DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.At, owner.zone).DateTime), e.Expiry, e.Future))) {
                var position = group.Key.Future ? owner.model.FutureDayPosition(group.Key.Day) : owner.model.PositionAt(group.First().At);
                var x = 1 + (float)(position / owner.model.ContentWidth * width);
                c.FillColor = Color.FromArgb(group.Key.Expiry ? "#EAAA51" : "#B5A0F4"); c.FillCircle(x, group.Key.Expiry ? 14 : 9, 2.5f);
            }
            var today = 1 + (float)owner.model.OverviewPositionAt(owner.now, width);
            c.StrokeColor = Color.FromArgb("#E5DCF9"); c.DrawLine(today, 3, today, 19);
            c.FillColor = Color.FromArgb("#D1BEFF");
            for (var x = -1; x <= 1; x++) for (var y = 0; y < 2; y++) c.FillCircle((float)(thumb.Left + thumb.Width / 2) + x * 4, 9 + y * 4, 1);
        }
    }
}
