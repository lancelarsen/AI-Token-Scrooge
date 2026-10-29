using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
namespace AIResetRadar.App.Controls;

public sealed class QuotaRowView : Border
{
    readonly Grid layout;
    readonly View quota;
    readonly View usage;
    readonly View? reset;
    readonly Label? countdown;
    readonly DateTimeOffset? resetsAt;
    readonly bool hasReset;
    readonly QuotaRow row;
    readonly UsageBar bar;
    readonly BoxView warningStrip;
    readonly Label? resetIcon;
    readonly AlertThresholdView thresholds;
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
    public QuotaRowView(QuotaRow row, Action<QuotaRow> open, bool capacityAlertsEnabled, Func<bool, Task<bool>> saveCapacityAlerts,
        Func<AlertThresholds, Task<bool>> saveThresholds, Action<bool> thresholdInteraction, TimeZoneInfo? timeZone = null)
    {
        this.row = row;
        var warning = row.IsWarning;
        var info = row.Window.Capacity(row.Low, row.Critical, row.Info) == CapacityState.Info;
        var critical = row.Window.Capacity(row.Low, row.Critical) is CapacityState.Critical or CapacityState.Exhausted;
        resetsAt = row.Window.ResetsAt;
        hasReset = resetsAt.HasValue;
        Stroke = Colors.Transparent; BackgroundColor = Colors.Transparent;
        Padding = new Thickness(0); MinimumHeightRequest = 56;
        var root = new Grid { ColumnDefinitions = [new(new GridLength(4)), new(GridLength.Star)] };
        warningStrip = new BoxView { Color = critical ? Color.FromArgb("#F05B69") : warning ? Color.FromArgb("#F2C94C") : info ? Color.FromArgb("#58A6FF") : Colors.Transparent };
        root.Add(warningStrip);
        layout = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        var quotaText = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        quotaText.Children.Add(new Label { Text = row.WindowTitle, TextColor = Color.FromArgb("#BEC1D2"), FontSize = 14 });
        if (row.CardNote != "") quotaText.Children.Add(new Label { Text = row.CardNote, FontSize = 11, TextColor = Color.FromArgb("#A5A8BA") });
        quota = quotaText;
        bar = new UsageBar(row.Window.Fill, row.UsedText, row.LeftText, warning, critical, info) { VerticalOptions = LayoutOptions.Center };
        var details = new Button { Text = "", BackgroundColor = Colors.Transparent, BorderWidth = 0, HeightRequest = -1,
            HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(details, $"{row.Account.Provider}, {row.AccountLabel}, {row.WindowTitle}, {row.UsedText}, {row.LeftText}. Open details.");
        details.Clicked += (_, _) => open(row);
        thresholds = new AlertThresholdView(row.Low, row.Critical, row.Info, capacityAlertsEnabled,
            $"{row.Account.Provider} / {row.AccountLabel} · {row.WindowTitle}", saveThresholds, thresholdInteraction);
        bar.AddAlertEditor(details, thresholds);
        var capacityToggle = new CapacityAlertToggle(capacityAlertsEnabled,
            $"Capacity Alerts · {row.Account.Provider} / {row.AccountLabel} · {row.WindowTitle}", async enabled => {
                if (!await saveCapacityAlerts(enabled)) return false;
                thresholds.SetAlertsEnabled(enabled); return true;
            });
        var usageArea = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 8 };
        usageArea.Add(capacityToggle); usageArea.Add(bar, 1); usage = usageArea;
        var now = DateTimeOffset.Now;
        if (hasReset)
        {
            countdown = new Label { Text = CountdownLabel(row.Window.ResetsAt, now), FontSize = 20, FontFamily = "Segoe UI Semibold", TextColor = Color.FromArgb("#BDA3F5") };
            var exactReset = new Label { Text = TimeZones.Format(row.Window.ResetsAt, timeZone ?? TimeZones.Resolve(null)), FontSize = 12,
                TextColor = Color.FromArgb("#BEC1D2"), VerticalOptions = LayoutOptions.Center };
            var resetText = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { countdown, exactReset } };
            resetIcon = new Label { Text = warning ? "\uE7BA" : "\uE823", FontFamily = "Segoe Fluent Icons", FontSize = 25,
                TextColor = Color.FromArgb(critical ? "#F05B69" : warning ? "#F2C94C" : info ? "#58A6FF" : "#B5A0F4"), VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 3, 0, 0) };
            reset = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center, Children = {
                resetIcon, resetText
            }};
        }
        else
        {
            countdown = null;
            reset = null;
        }
        root.Add(layout, 1); Content = root;
        // Keep the alert circle independent of the details hit target.
        foreach (var part in new[] { quota, reset }.OfType<View>())
        {
            var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => open(row); part.GestureRecognizers.Add(tap);
        }
        SetWideLayout(false);
    }

    public void UpdateAlertRules(RadarSettings settings)
    {
        var rule = settings.RuleFor(row.Account.Id, row.Window.Id);
        var capacity = row.Window.Capacity(rule.LowThreshold!.Value, rule.CriticalThreshold!.Value, rule.InfoThreshold!.Value);
        var info = capacity == CapacityState.Info;
        var critical = capacity is CapacityState.Critical or CapacityState.Exhausted;
        var warning = capacity is CapacityState.Low or CapacityState.Critical or CapacityState.Exhausted;
        var enabled = settings.CapacityAlertsEnabled(row.Account.Id, row.Window.Id);
        thresholds.Refresh(rule.LowThreshold!.Value, rule.CriticalThreshold!.Value, rule.InfoThreshold!.Value, enabled);
        bar.SetWarning(warning, critical, info);
        warningStrip.Color = critical ? Color.FromArgb("#F05B69") : warning ? Color.FromArgb("#F2C94C") : info ? Color.FromArgb("#58A6FF") : Colors.Transparent;
        if (resetIcon is not null)
        {
            resetIcon.Text = warning ? "\uE7BA" : "\uE823";
            resetIcon.TextColor = Color.FromArgb(critical ? "#F05B69" : warning ? "#F2C94C" : info ? "#58A6FF" : "#B5A0F4");
        }
    }

    public void SetWideLayout(bool wide)
    {
        if (wideLayout == wide) return;
        wideLayout = wide;
        layout.Children.Clear(); layout.ColumnDefinitions.Clear(); layout.RowDefinitions.Clear();
        Grid.SetColumnSpan(quota, 1); Grid.SetColumnSpan(usage, 1);
        if (wide)
        {
            // Keep the reset column even when this particular window has no reset.
            layout.ColumnDefinitions = [new(new GridLength(156)), new(GridLength.Star), new(new GridLength(200))];
            layout.Add(quota); layout.Add(usage, 1);
            if (reset is not null) layout.Add(reset, 2);
        }
        else
        {
            layout.ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)];
            layout.RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)];
            layout.Add(quota, 0, 0);
            if (reset is not null) layout.Add(reset, 1, 0);
            layout.Add(usage, 0, 1);
            Grid.SetColumnSpan(quota, hasReset ? 1 : 2);
            Grid.SetColumnSpan(usage, 2);
        }
    }
}
