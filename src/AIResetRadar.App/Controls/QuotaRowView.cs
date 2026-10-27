using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
namespace AIResetRadar.App.Controls;

public sealed class QuotaRowView : Border
{
    readonly Grid layout;
    readonly View quota;
    readonly UsageBar bar;
    readonly View? reset;
    readonly Label? countdown;
    readonly DateTimeOffset? resetsAt;
    readonly bool hasReset;
    bool? wideLayout;
    public void UpdateClock(DateTimeOffset now)
    {
        if (countdown is not null) countdown.Text = CountdownLabel(resetsAt, now);
    }
    static string CountdownLabel(DateTimeOffset? reset, DateTimeOffset now)
    {
        var summary = ResetTime.Summary(reset, now);
        return summary.StartsWith("Resets: ", StringComparison.Ordinal) ? summary[8..] : summary;
    }
    public QuotaRowView(QuotaRow row, Action<QuotaRow> open, TimeZoneInfo? timeZone = null)
    {
        var warning = row.IsWarning;
        resetsAt = row.Window.ResetsAt;
        hasReset = resetsAt.HasValue;
        Stroke = Colors.Transparent; BackgroundColor = Colors.Transparent;
        Padding = new Thickness(0); MinimumHeightRequest = 56;
        var root = new Grid { ColumnDefinitions = [new(new GridLength(4)), new(GridLength.Star)] };
        root.Add(new BoxView { Color = warning ? Color.FromArgb("#EAAA51") : Colors.Transparent });
        layout = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        var quotaText = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        quotaText.Children.Add(new Label { Text = row.WindowTitle, TextColor = Color.FromArgb("#BEC1D2"), FontSize = 14 });
        if (row.CardNote != "") quotaText.Children.Add(new Label { Text = row.CardNote, FontSize = 11, TextColor = Color.FromArgb("#A5A8BA") });
        quota = quotaText;
        bar = new UsageBar(row.Window.Fill, row.UsedText, row.LeftText, warning) { VerticalOptions = LayoutOptions.Center };
        var now = DateTimeOffset.Now;
        if (hasReset)
        {
            countdown = new Label { Text = CountdownLabel(row.Window.ResetsAt, now), FontSize = 20, FontFamily = "Segoe UI Semibold", TextColor = Color.FromArgb("#BDA3F5") };
            var exactReset = new Label { Text = TimeZones.Format(row.Window.ResetsAt, timeZone ?? TimeZones.Resolve(null)), FontSize = 12,
                TextColor = Color.FromArgb("#BEC1D2"), VerticalOptions = LayoutOptions.Center };
            var resetText = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { countdown, exactReset } };
            reset = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center, Children = {
                new Label { Text = warning ? "\uE7BA" : "\uE823", FontFamily = "Segoe Fluent Icons", FontSize = 25,
                    TextColor = Color.FromArgb(warning ? "#EAAA51" : "#B5A0F4"), VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 3, 0, 0) },
                resetText
            }};
        }
        else
        {
            countdown = null;
            reset = null;
        }
        root.Add(layout, 1); Content = root;
        // Native button is transparent, keyboard-focusable and exposes the complete row to Narrator.
        var details = new Button { Text = "", BackgroundColor = Colors.Transparent, BorderWidth = 0, HeightRequest = -1, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(details, $"{row.Account.Provider}, {row.AccountLabel}, {row.WindowTitle}, {row.UsedText}, {row.LeftText}. Open details.");
        details.Clicked += (_, _) => open(row); root.Children.Add(details); Grid.SetColumnSpan(details, 2);
        SetWideLayout(false);
    }

    public void SetWideLayout(bool wide)
    {
        if (wideLayout == wide) return;
        wideLayout = wide;
        layout.Children.Clear(); layout.ColumnDefinitions.Clear(); layout.RowDefinitions.Clear();
        Grid.SetColumnSpan(quota, 1); Grid.SetColumnSpan(bar, 1);
        if (wide)
        {
            // Keep the reset column even when this particular window has no reset.
            layout.ColumnDefinitions = [new(new GridLength(156)), new(GridLength.Star), new(new GridLength(200))];
            layout.Add(quota); layout.Add(bar, 1);
            if (reset is not null) layout.Add(reset, 2);
        }
        else
        {
            layout.ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)];
            layout.RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)];
            layout.Add(quota, 0, 0);
            if (reset is not null) layout.Add(reset, 1, 0);
            layout.Add(bar, 0, 1);
            Grid.SetColumnSpan(quota, hasReset ? 1 : 2);
            Grid.SetColumnSpan(bar, 2);
        }
    }
}
