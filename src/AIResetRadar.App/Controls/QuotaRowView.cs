using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
namespace AIResetRadar.App.Controls;

public sealed class QuotaRowView : Border
{
    readonly Grid layout;
    readonly View identity;
    readonly UsageBar bar;
    readonly View reset;
    readonly Label countdown;
    readonly DateTimeOffset? resetsAt;
    bool? wideLayout;
    public void UpdateClock(DateTimeOffset now) => countdown.Text = ResetTime.Countdown(resetsAt, now);
    public QuotaRowView(QuotaRow row, Action<QuotaRow> open, bool grouped = false, bool showAccount = true)
    {
        var warning = row.IsWarning;
        resetsAt = row.Window.ResetsAt;
        Stroke = Color.FromArgb("#383B46"); BackgroundColor = Color.FromArgb("#1C1E24");
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 13 };
        if (grouped) { Stroke = Colors.Transparent; BackgroundColor = Colors.Transparent; }
        Padding = new Thickness(0); MinimumHeightRequest = 102;
        var root = new Grid { ColumnDefinitions = [new(new GridLength(4)), new(GridLength.Star)] };
        root.Add(new BoxView { Color = warning ? Color.FromArgb("#EAAA51") : Colors.Transparent });
        layout = new Grid { Padding = new Thickness(20, 15), ColumnSpacing = 24, RowSpacing = 12 };
        var accountText = new VerticalStackLayout { Spacing = 4 };
        var heading = new HorizontalStackLayout { Spacing = 10 };
        heading.Children.Add(new Label { Text = row.Account.Provider, FontSize = 20, FontFamily = "Segoe UI Semibold", VerticalOptions = LayoutOptions.Center });
        heading.Children.Add(new Border { Padding = new Thickness(11, 3), Stroke = Color.FromArgb("#51476D"), BackgroundColor = Color.FromArgb("#2C293E"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 },
            Content = new Label { Text = row.AccountLabel, TextColor = Color.FromArgb("#C3ADF8"), FontSize = 12 } });
        if (showAccount) accountText.Children.Add(heading);
        accountText.Children.Add(new Label { Text = row.Window.Name, TextColor = Color.FromArgb("#BEC1D2"), FontSize = 14 });
        if (row.CardNote != "") accountText.Children.Add(new Label { Text = row.CardNote, FontSize = 11, TextColor = Color.FromArgb("#A5A8BA") });
        var account = new HorizontalStackLayout { Spacing = 16, VerticalOptions = LayoutOptions.Center };
        if (showAccount) account.Children.Add(row.Account.Provider == "OpenAI" ? new Image { Source = "openai.png", WidthRequest = 44, HeightRequest = 44 } :
            new Image { Source = row.Account.Provider == "Claude" ? "claude.png" : "github.png", WidthRequest = 44, HeightRequest = 44 });
        else account.Children.Add(new BoxView { WidthRequest = 44, HeightRequest = 1, Color = Colors.Transparent });
        account.Children.Add(accountText); identity = account;
        bar = new UsageBar(row.Window.Fill, row.UsedText, row.LeftText, warning) { VerticalOptions = LayoutOptions.Center };
        var now = DateTimeOffset.Now;
        countdown = new Label { Text = ResetTime.Countdown(row.Window.ResetsAt, now), FontSize = 20, FontFamily = "Segoe UI Semibold", TextColor = Color.FromArgb("#BDA3F5") };
        reset = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center, Children = {
            new Label { Text = warning ? "\uE7BA" : "\uE823", FontFamily = "Segoe Fluent Icons", FontSize = 25,
                TextColor = Color.FromArgb(warning ? "#EAAA51" : "#B5A0F4"), VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 3, 0, 0) },
            new VerticalStackLayout { Spacing = 3, Children = {
                new Label { Text = "Resets", FontSize = 12, TextColor = Color.FromArgb("#BEC1D2") },
                countdown,
                new Label { Text = row.Window.ResetsAt?.ToLocalTime().ToString("ddd, MMM d · h:mm tt zzz") ?? "Not supplied by provider", FontSize = 12, TextColor = Color.FromArgb("#BEC1D2") }
            }}
        }};
        root.Add(layout, 1); Content = root;
        // Native button is transparent, keyboard-focusable and exposes the complete row to Narrator.
        var details = new Button { Text = "", BackgroundColor = Colors.Transparent, BorderWidth = 0, HeightRequest = -1, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(details, $"{row.Account.Provider}, {row.AccountLabel}, {row.Window.Name}, {row.UsedText}, {row.LeftText}. Open details.");
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
        Grid.SetColumnSpan(bar, 1);
        if (wide)
        {
            layout.ColumnDefinitions = [new(new GridLength(320)), new(GridLength.Star), new(new GridLength(235))];
            layout.Add(identity); layout.Add(bar, 1); layout.Add(reset, 2);
        }
        else
        {
            layout.ColumnDefinitions = [new(GridLength.Star), new(new GridLength(235))];
            layout.RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)];
            layout.Add(identity); layout.Add(reset, 1); layout.Add(bar, 0, 1); Grid.SetColumnSpan(bar, 2);
        }
    }
}
