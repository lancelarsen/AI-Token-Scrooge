using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
namespace AIResetRadar.App.Controls;

public sealed class QuotaRowView : Border
{
    readonly Grid layout;
    readonly View identity;
    readonly View quota;
    readonly UsageBar bar;
    readonly View? reset;
    readonly Label? countdown;
    readonly Label? exactReset;
    readonly DateTimeOffset? resetsAt;
    readonly bool hasReset;
    bool? wideLayout;
    public void UpdateClock(DateTimeOffset now)
    {
        if (countdown is not null) countdown.Text = ResetTime.Summary(resetsAt, now);
    }
    public QuotaRowView(QuotaRow row, Action<QuotaRow> open, bool grouped = false, bool showAccount = true, TimeZoneInfo? timeZone = null)
    {
        var warning = row.IsWarning;
        resetsAt = row.Window.ResetsAt;
        hasReset = resetsAt.HasValue;
        Stroke = Color.FromArgb("#383B46"); BackgroundColor = Color.FromArgb("#1C1E24");
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 13 };
        if (grouped) { Stroke = Colors.Transparent; BackgroundColor = Colors.Transparent; }
        Padding = new Thickness(0); MinimumHeightRequest = 102;
        var root = new Grid { ColumnDefinitions = [new(new GridLength(4)), new(GridLength.Star)] };
        root.Add(new BoxView { Color = warning ? Color.FromArgb("#EAAA51") : Colors.Transparent });
        layout = new Grid { Padding = new Thickness(20, 15), ColumnSpacing = 20, RowSpacing = 10 };
        if (showAccount)
        {
            var providerText = new VerticalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center };
            providerText.Children.Add(new Label { Text = row.Account.Provider, FontSize = 20, FontFamily = "Segoe UI Semibold" });
            providerText.Children.Add(new Border { Padding = new Thickness(11, 3), HorizontalOptions = LayoutOptions.Start,
                Stroke = Color.FromArgb("#51476D"), BackgroundColor = Color.FromArgb("#2C293E"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 },
                Content = new Label { Text = row.AccountLabel, TextColor = Color.FromArgb("#C3ADF8"), FontSize = 12 } });
            identity = new HorizontalStackLayout { Spacing = 16, VerticalOptions = LayoutOptions.Center, Children = {
                row.Account.Provider == "OpenAI" ? new Image { Source = "openai.png", WidthRequest = 44, HeightRequest = 44 } :
                    new Image { Source = row.Account.Provider == "Claude" ? "claude.png" : "github.png", WidthRequest = 44, HeightRequest = 44 },
                providerText
            }};
        }
        else identity = new BoxView { Color = Colors.Transparent };
        var quotaText = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        quotaText.Children.Add(new Label { Text = row.WindowTitle, TextColor = Color.FromArgb("#BEC1D2"), FontSize = 14 });
        if (row.CardNote != "") quotaText.Children.Add(new Label { Text = row.CardNote, FontSize = 11, TextColor = Color.FromArgb("#A5A8BA") });
        quota = quotaText;
        bar = new UsageBar(row.Window.Fill, row.UsedText, row.LeftText, warning) { VerticalOptions = LayoutOptions.Center };
        var now = DateTimeOffset.Now;
        if (hasReset)
        {
            countdown = new Label { Text = ResetTime.Summary(row.Window.ResetsAt, now), FontSize = 20, FontFamily = "Segoe UI Semibold", TextColor = Color.FromArgb("#BDA3F5") };
            exactReset = new Label { Text = TimeZones.Format(row.Window.ResetsAt, timeZone ?? TimeZones.Resolve(null)), FontSize = 12,
                TextColor = Color.FromArgb("#BEC1D2"), VerticalOptions = LayoutOptions.Center };
            reset = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center, Children = {
                new Label { Text = warning ? "\uE7BA" : "\uE823", FontFamily = "Segoe Fluent Icons", FontSize = 25,
                    TextColor = Color.FromArgb(warning ? "#EAAA51" : "#B5A0F4"), VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 3, 0, 0) },
                countdown
            }};
        }
        else
        {
            countdown = null;
            exactReset = null;
            reset = null;
        }
        root.Add(layout, 1); Content = root;
        // Native button is transparent, keyboard-focusable and exposes the complete row to Narrator.
        var details = new Button { Text = "", BackgroundColor = Colors.Transparent, BorderWidth = 0, HeightRequest = -1, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(details, $"{row.Account.Provider}, {row.AccountLabel}, {row.WindowTitle}, {row.UsedText}, {row.LeftText}. Open details.");
        details.Clicked += (_, _) => open(row); root.Children.Add(details); Grid.SetColumnSpan(details, 2);
        SizeChanged += (_, _) => Reflow(); Reflow();
    }

    void Reflow()
    {
        var wide = Width > 1050;
        // Height changes are a result of reflow, not a reason to rebuild the same tree again.
        if (wideLayout == wide) return;
        wideLayout = wide;
        layout.Children.Clear(); layout.ColumnDefinitions.Clear(); layout.RowDefinitions.Clear();
        Grid.SetColumnSpan(identity, 1); Grid.SetColumnSpan(quota, 1); Grid.SetColumnSpan(bar, 1);
        if (wide)
        {
            layout.ColumnDefinitions = hasReset
                ? [new(new GridLength(250)), new(new GridLength(180)), new(GridLength.Star), new(new GridLength(200)), new(new GridLength(165))]
                : [new(new GridLength(250)), new(new GridLength(180)), new(GridLength.Star)];
            layout.Add(identity); layout.Add(quota, 1); layout.Add(bar, 2);
            if (reset is not null) layout.Add(reset, 3);
            if (exactReset is not null) layout.Add(exactReset, 4);
        }
        else
        {
            layout.ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)];
            layout.RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)];
            layout.Add(identity, 0, 0);
            if (reset is not null) layout.Add(reset, 1, 0);
            layout.Add(quota, 0, 1);
            if (exactReset is not null) layout.Add(exactReset, 1, 1);
            layout.Add(bar, 0, 2);
            Grid.SetColumnSpan(identity, hasReset ? 1 : 2);
            Grid.SetColumnSpan(quota, hasReset ? 1 : 2);
            Grid.SetColumnSpan(bar, 2);
        }
    }
}
