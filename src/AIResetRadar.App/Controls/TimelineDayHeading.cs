namespace AIResetRadar.App.Controls;

/// <summary>Shared native typography and spacing for both horizon modes.</summary>
public sealed class TimelineDayHeading : VerticalStackLayout
{
    readonly Label day;
    readonly DateOnly date;

    public TimelineDayHeading(DateOnly date, bool today)
    {
        this.date = date;
        Spacing = 7;
        day = new Label { FontSize = 14, FontFamily = "Segoe UI Semibold", HorizontalTextAlignment = TextAlignment.Center };
        Children.Add(day);
        Children.Add(new Label { Text = date.ToString("MMM d"), FontSize = 12, FontFamily = "Segoe UI",
            TextColor = Color.FromArgb("#BEC1D2"), HorizontalTextAlignment = TextAlignment.Center });
        UpdateToday(today);
    }

    public void UpdateToday(bool today)
    {
        day.Text = today ? "Today" : date.ToString("ddd");
        day.TextColor = Color.FromArgb(today ? "#D1BEFF" : "#B5A0F4");
    }
}
