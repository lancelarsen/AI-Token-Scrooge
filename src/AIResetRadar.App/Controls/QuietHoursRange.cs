namespace AIResetRadar.App.Controls;

// Noon-to-noon keeps the usual overnight quiet interval continuous on the track.
public sealed class QuietHoursRange : ContentView, IDrawable
{
    readonly GraphicsView track;
    readonly Picker start;
    readonly Picker end;
    readonly Label summary;
    bool movingStart;
    bool dragging;
    public int StartHour => Math.Max(0, start.SelectedIndex);
    public int EndHour => Math.Max(0, end.SelectedIndex);
    static double Position(int hour, bool end = false) => end && hour == 12 ? 1 : ((hour + 12) % 24) / 24d;
    static string Time(int hour) => new DateTime(2026, 1, 1, hour, 0, 0).ToString("h tt");

    public QuietHoursRange(int startHour, int endHour)
    {
        var times = Enumerable.Range(0, 24).Select(Time).ToArray();
        start = new Picker { ItemsSource = times, SelectedIndex = Math.Clamp(startHour, 0, 23), TextColor = Color.FromArgb("#C3ADF8"), FontSize = 18 };
        end = new Picker { ItemsSource = times, SelectedIndex = Math.Clamp(endHour, 0, 23), TextColor = Color.FromArgb("#C3ADF8"), FontSize = 18 };
        SemanticProperties.SetDescription(start, "Quiet hours start time");
        SemanticProperties.SetDescription(end, "Quiet hours end time");
        var labels = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], ColumnSpacing = 24 };
        labels.Add(new VerticalStackLayout { Spacing = 2, Children = { new Label { Text = "FROM", FontSize = 11, TextColor = Color.FromArgb("#A5A8BA") }, start } });
        labels.Add(new VerticalStackLayout { Spacing = 2, Children = { new Label { Text = "UNTIL", FontSize = 11, TextColor = Color.FromArgb("#A5A8BA") }, end } }, 1);
        track = new GraphicsView { Drawable = this, HeightRequest = 88 };
        SemanticProperties.SetDescription(track, "Drag either circle to change quiet hours. The From and Until controls also support keyboard selection.");
        track.StartInteraction += (_, e) =>
        {
            if (!IsEnabled || e.Touches.Length == 0 || track.Width <= 48) return;
            var x = e.Touches[0].X;
            var a = 24 + Position(StartHour) * (track.Width - 48);
            var b = 24 + Position(EndHour, true) * (track.Width - 48);
            movingStart = Math.Abs(x - a) == Math.Abs(x - b) ? !movingStart : Math.Abs(x - a) < Math.Abs(x - b);
            dragging = true; Move(x);
        };
        track.DragInteraction += (_, e) => { if (dragging && IsEnabled && e.Touches.Length > 0) Move(e.Touches[0].X); };
        track.EndInteraction += (_, _) => dragging = false;
        track.CancelInteraction += (_, _) => dragging = false;
        summary = new Label { FontSize = 13, TextColor = Color.FromArgb("#BEC1D2") };
        start.SelectedIndexChanged += (_, _) => Refresh(); end.SelectedIndexChanged += (_, _) => Refresh();
        Content = new VerticalStackLayout { Spacing = 6, Children = { labels, track, summary } };
        Refresh();
    }
    void Move(float x)
    {
        var offset = (int)Math.Round(Math.Clamp((x - 24) / Math.Max(1, track.Width - 48), 0, 1) * 24);
        var hour = (offset + 12) % 24;
        if (movingStart) start.SelectedIndex = hour; else end.SelectedIndex = hour;
    }
    void Refresh()
    {
        var duration = (EndHour - StartHour + 24) % 24;
        summary.Text = duration == 0 ? "Choose different start and end times to set a quiet period." :
            $"{Time(StartHour)} – {Time(EndHour)} · {duration} hours{(EndHour < StartHour ? " · Overnight" : "")} · Local time";
        track.Invalidate();
    }
    public void Draw(ICanvas canvas, RectF bounds)
    {
        var left = 24f; var right = Math.Max(left, bounds.Width - 24); var span = right - left; const float y = 28;
        var a = left + (float)Position(StartHour) * span; var b = left + (float)Position(EndHour, true) * span;
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
        canvas.FontColor = Color.FromArgb("#A5A8BA"); canvas.FontSize = 11;
        string[] ticks = ["12 PM", "6 PM", "12 AM", "6 AM", "12 PM"];
        for (var i = 0; i < ticks.Length; i++)
            canvas.DrawString(ticks[i], left + span * i / 4 - 24, 55, 48, 20, Microsoft.Maui.Graphics.HorizontalAlignment.Center, Microsoft.Maui.Graphics.VerticalAlignment.Center);
    }
}
