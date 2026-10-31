namespace AIResetRadar.App.Controls;

// Noon-to-noon keeps the usual overnight quiet interval continuous on the track.
public sealed class QuietHoursRange : ContentView, IDrawable
{
    readonly GraphicsView track;
    readonly Button start;
    readonly Button end;
    readonly AbsoluteLayout range = new();
    readonly Label summary;
    bool movingStart;
    bool dragging;
    public event EventHandler? Changed;
    public int StartHour { get; private set; }
    public int EndHour { get; private set; }
    const int TimeFontSize = 16;
    const int TrackInset = 32;
    static double Position(int hour, bool end = false) => end && hour == 12 ? 1 : ((hour + 12) % 24) / 24d;
    static string Time(int hour) => new DateTime(2026, 1, 1, hour, 0, 0).ToString("h tt");

    public QuietHoursRange(int startHour, int endHour)
    {
        StartHour = Math.Clamp(startHour, 0, 23); EndHour = Math.Clamp(endHour, 0, 23);
        start = TimeLabel(true); end = TimeLabel(false);
        SettingsHelp.Attach(start, "Quiet Hours Start — Drag the Circle or Use Arrow Keys in One-Hour Steps");
        SettingsHelp.Attach(end, "Quiet Hours End — Drag the Circle or Use Arrow Keys in One-Hour Steps");
        track = new GraphicsView { Drawable = this, HeightRequest = 94 };
        SemanticProperties.SetDescription(track, "Drag either circle to change quiet hours. Focus a time label and use arrow keys to adjust it.");
        track.StartInteraction += (_, e) =>
        {
            if (!IsEnabled || e.Touches.Length == 0 || track.Width <= TrackInset * 2) return;
            var x = e.Touches[0].X;
            var a = TrackInset + Position(StartHour) * (track.Width - TrackInset * 2);
            var b = TrackInset + Position(EndHour, true) * (track.Width - TrackInset * 2);
            movingStart = Math.Abs(x - a) == Math.Abs(x - b) ? !movingStart : Math.Abs(x - a) < Math.Abs(x - b);
            dragging = true; Move(x);
        };
        track.DragInteraction += (_, e) => { if (dragging && IsEnabled && e.Touches.Length > 0) Move(e.Touches[0].X); };
        track.EndInteraction += (_, _) => { dragging = false; Changed?.Invoke(this, EventArgs.Empty); };
        track.CancelInteraction += (_, _) => { dragging = false; Changed?.Invoke(this, EventArgs.Empty); };
        summary = new Label { FontSize = TimeFontSize, TextColor = Color.FromArgb("#C3ADF8") };
        range.HeightRequest = 94;
        range.Children.Add(track);
        AbsoluteLayout.SetLayoutFlags(track, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(track, new Rect(0, 0, 1, 1));
        range.Children.Add(start); range.Children.Add(end);
        range.SizeChanged += (_, _) => PositionLabels();
        Content = new VerticalStackLayout { Spacing = 4, Children = { range, summary } };
        Refresh();
    }
    void Move(float x)
    {
        var offset = (int)Math.Round(Math.Clamp((x - TrackInset) / Math.Max(1, track.Width - TrackInset * 2), 0, 1) * 24);
        var hour = (offset + 12) % 24;
        if (movingStart) StartHour = hour; else EndHour = hour;
        Refresh();
    }
    void Refresh()
    {
        var duration = (EndHour - StartHour + 24) % 24;
        summary.Text = duration == 0 ? "Choose different start and end times to set a quiet period." :
            $"{Time(StartHour)} – {Time(EndHour)} · {duration} hours{(EndHour < StartHour ? " · Overnight" : "")}";
        start.Text = Time(StartHour); end.Text = Time(EndHour);
        SemanticProperties.SetDescription(start, $"Quiet Hours Start Time: {Time(StartHour)}. Use arrow keys to adjust.");
        SemanticProperties.SetDescription(end, $"Quiet Hours End Time: {Time(EndHour)}. Use arrow keys to adjust.");
        PositionLabels();
        track.Invalidate();
    }
    Button TimeLabel(bool isStart)
    {
        var label = new Button { FontSize = TimeFontSize, TextColor = Color.FromArgb("#F2F1F5"),
            BackgroundColor = Colors.Transparent, BorderWidth = 0, Padding = 0, HeightRequest = 32, WidthRequest = 80 };
        label.HandlerChanged += (_, _) => {
            if (label.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.Button native) return;
            native.KeyDown += (_, args) => {
                if (!IsEnabled) return;
                var change = args.Key switch { Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Down => -1,
                    Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up => 1, _ => 0 };
                if (change == 0) return;
                var hour = ((isStart ? StartHour : EndHour) + change + 24) % 24;
                if (isStart) StartHour = hour; else EndHour = hour;
                args.Handled = true; Refresh(); Changed?.Invoke(this, EventArgs.Empty);
            };
        };
        return label;
    }
    void PositionLabels()
    {
        if (range.Width < 80) return;
        var a = TrackInset + Position(StartHour) * (range.Width - TrackInset * 2);
        var b = TrackInset + Position(EndHour, true) * (range.Width - TrackInset * 2);
        var close = Math.Abs(a - b) < 80;
        range.HeightRequest = track.HeightRequest = close ? 116 : 94;
        AbsoluteLayout.SetLayoutBounds(start, new Rect(Math.Clamp(a - 40, 0, range.Width - 80), 0, 80, 32));
        AbsoluteLayout.SetLayoutBounds(end, new Rect(Math.Clamp(b - 40, 0, range.Width - 80), close ? 22 : 0, 80, 32));
    }
    public void Draw(ICanvas canvas, RectF bounds)
    {
        var left = (float)TrackInset; var right = Math.Max(left, bounds.Width - TrackInset); var span = right - left;
        var a = left + (float)Position(StartHour) * span; var b = left + (float)Position(EndHour, true) * span;
        var y = Math.Abs(a - b) < 80 ? 66f : 44f;
        canvas.StrokeLineCap = LineCap.Round; canvas.StrokeSize = 6; canvas.StrokeColor = Color.FromArgb("#41434F");
        canvas.DrawLine(left, y, right, y);
        canvas.StrokeColor = Color.FromArgb("#A990ED");
        if (StartHour != EndHour)
        {
            if (a < b) canvas.DrawLine(a, y, b, y);
            else { canvas.DrawLine(a, y, right, y); canvas.DrawLine(left, y, b, y); }
        }
        foreach (var x in new[] { a, b })
        {
            canvas.FillColor = Color.FromArgb("#C3ADF8"); canvas.FillCircle(x, y, 12);
            canvas.StrokeSize = 3; canvas.StrokeColor = Color.FromArgb("#7160B4"); canvas.DrawCircle(x, y, 12);
        }
        canvas.FontColor = Color.FromArgb("#C3ADF8"); canvas.FontSize = TimeFontSize;
        string[] ticks = ["12 PM", "6 PM", "12 AM", "6 AM", "12 PM"];
        for (var i = 0; i < ticks.Length; i++)
            canvas.DrawString(ticks[i], left + span * i / 4 - 32, y + 22, 64, 24, Microsoft.Maui.Graphics.HorizontalAlignment.Center, Microsoft.Maui.Graphics.VerticalAlignment.Center);
    }
}
