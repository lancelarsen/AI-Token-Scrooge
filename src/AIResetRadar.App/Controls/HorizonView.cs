using Microsoft.Maui.Layouts;
using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
namespace AIResetRadar.App.Controls;

public sealed class HorizonView : Border
{
    readonly HistoryTimelineView history;
    readonly Switch historyToggle = new() { OnColor = Color.FromArgb("#8670CC"), ThumbColor = Color.FromArgb("#D1BEFF"), IsToggled = true };
    readonly Label refreshLabel = new();
    readonly Label expiryLabel = new();
    bool updating;
    readonly Label hint = new() { Text = "Hover or click for details.", FontSize = 13, TextColor = Color.FromArgb("#BEC1D2"), Margin = new Thickness(0, 8, 0, 8), VerticalOptions = LayoutOptions.Center };
    public HorizonView(IEnumerable<HorizonEvent> source, Action<HorizonEvent> open, TimeZoneInfo? timeZone = null, Action? settings = null,
        Func<bool, Task<bool>>? toggleHistory = null, Func<string, bool, Task<bool>>? selectAccount = null)
    {
        history = new HistoryTimelineView(open, selectAccount ?? ((_, _) => Task.FromResult(true)));
        var events = source.ToArray();
        BackgroundColor = Color.FromArgb("#1C1E24"); StrokeThickness = 1; Stroke = Color.FromArgb("#383B46");
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 };
        Padding = new Thickness(12, 18);
        var header = new FlexLayout { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap, AlignItems = FlexAlignItems.Center, Margin = new Thickness(12, 0) };
        header.Children.Add(new HorizontalStackLayout { Spacing = 12, Margin = new Thickness(0, 0, 22, 0), Children = {
            new Label { Text = "\uE81C", FontFamily = "Segoe Fluent Icons", FontSize = 30, TextColor = Color.FromArgb("#B5A0F4"), VerticalOptions = LayoutOptions.Center },
            new Label { Text = "Reset Horizon", FontSize = 26, FontFamily = "Segoe UI Semibold", VerticalOptions = LayoutOptions.Center }
        } });
        var refreshCount = events.Count(e => !e.IsExpiry);
        var expiryCount = events.Count(e => e.IsExpiry);
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
        Content = new VerticalStackLayout { Spacing = 16, Children = {
            headerRow, new BoxView { HeightRequest = 1, Color = Color.FromArgb("#383B46"), Margin = new Thickness(12, 0) }, history
        } };
        SetHistoryVisible(true);
    }
    public void Update(IEnumerable<HorizonEvent> source, HistorySeries[] series, HistoryPreferences choices, TimeZoneInfo zone,
        DateTimeOffset now, bool loading = false, string? error = null)
    {
        var events = source.ToArray();
        refreshLabel.Text = $"Quota Refresh ({events.Count(e => !e.IsExpiry)})";
        expiryLabel.Text = $"Reset Expires ({events.Count(e => e.IsExpiry)})";
        updating = true; historyToggle.IsToggled = choices.Enabled; updating = false;
        SetHistoryVisible(choices.Enabled);
        history.Update(series, events, choices, zone, now, loading, error);
        hint.Text = choices.Enabled ? "Scroll Left for History" : "Hover or Click for Details";
    }
    void SetHistoryVisible(bool enabled) => history.SetHistoryEnabled(enabled);
    static View Legend(Label label, string color)
    {
        label.FontSize = 13; label.TextColor = Color.FromArgb("#BEC1D2"); label.VerticalOptions = LayoutOptions.Center;
        return new HorizontalStackLayout { Spacing = 7, Margin = new Thickness(0, 8, 20, 8), VerticalOptions = LayoutOptions.Center, Children = {
        new Border { WidthRequest = 10, HeightRequest = 10, StrokeThickness = 0, BackgroundColor = Color.FromArgb(color), VerticalOptions = LayoutOptions.Center,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 } },
        label
    } };
    }
}
