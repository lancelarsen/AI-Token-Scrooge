using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
namespace AIResetRadar.App.Controls;

public sealed class QuotaRowView : Border
{
    readonly Grid layout;
    readonly View quota;
    readonly View usage;
    readonly HorizontalStackLayout reset;
    readonly Label countdown = new() { FontSize = ResetCountdown.Size, FontFamily = "Segoe UI Semibold" };
    readonly Label exactReset = new() { FontSize = ResetCountdown.DateSize, TextColor = Color.FromArgb(ResetCountdown.DateColor) };
    readonly TimeZoneInfo zone;
    (DateTimeOffset? At, bool IsExpiry) deadline;
    readonly QuotaRow row;
    readonly UsageBar bar;
    RadarSettings alertSettings = new();
    readonly Label resetIcon = new() { FontFamily = "Segoe Fluent Icons", FontSize = ResetCountdown.IconSize, VerticalOptions = LayoutOptions.Start, Margin = new Thickness(0, 3, 0, 0) };
    readonly AlertThresholdView thresholds;
    bool? wideLayout;
    public void UpdateClock(DateTimeOffset now)
    {
        var selected = ResetCountdown.Select(row, now);
        reset.IsVisible = selected.At.HasValue;
        if (wideLayout == false) Grid.SetColumnSpan(quota, reset.IsVisible ? 1 : 2);
        if (selected != deadline)
        {
            deadline = selected;
            var kind = deadline.IsExpiry ? "Earned Reset Expires" : "Quota Refresh";
            exactReset.Text = TimeZones.Format(deadline.At, zone);
            resetIcon.Scale = ResetCountdown.IconScale(deadline.IsExpiry);
            resetIcon.Text = ResetCountdown.Glyph(deadline.IsExpiry);
            countdown.TextColor = Color.FromArgb(ResetCountdown.TextColor(deadline.IsExpiry));
            ToolTipProperties.SetText(reset, kind);
            SemanticProperties.SetDescription(reset, $"{kind}, {exactReset.Text}");
        }
        if (selected.At is not { } at) return;
        countdown.Text = ResetCountdown.Label(at, now);
        var severity = alertSettings.CapacityAlertsEnabled(row.Account.Id, row.Window.Id)
            ? TimedReminder.Due(alertSettings.EffectiveRefreshReminders, at, now)?.Severity : null;
        resetIcon.TextColor = Color.FromArgb(selected.IsExpiry ? ResetCountdown.ExpiryColor : severity switch
        {
            ReminderSeverity.Critical => "#F05B69", ReminderSeverity.Warning => "#F2C94C",
            ReminderSeverity.Info => "#58A6FF", _ => "#B5A0F4"
        });
    }
    public QuotaRowView(QuotaRow row, Action<QuotaRow> open, Action openReserve, bool capacityAlertsEnabled, Func<bool, Task<bool>> saveCapacityAlerts,
        Func<AlertThresholds, Task<bool>> saveThresholds, Action<bool> thresholdInteraction, TimeZoneInfo? timeZone = null)
    {
        this.row = row;
        var warning = row.IsWarning;
        var info = row.Window.Capacity(row.Low, row.Critical, row.Info) == CapacityState.Info;
        var critical = row.Window.Capacity(row.Low, row.Critical) is CapacityState.Critical or CapacityState.Exhausted;
        zone = timeZone ?? TimeZones.Resolve(null);
        Stroke = Colors.Transparent; BackgroundColor = Colors.Transparent;
        Padding = new Thickness(0); MinimumHeightRequest = 56;
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
            $"{row.Account.Provider} / {row.AccountLabel} · {row.WindowTitle}", saveThresholds, thresholdInteraction, row.Window.Fill);
        bar.AddAlertEditor(details, thresholds);
        var capacityToggle = new CapacityAlertToggle(capacityAlertsEnabled,
            $"Row Alerts · {row.Account.Provider} / {row.AccountLabel} · {row.WindowTitle}", async enabled => {
                if (!await saveCapacityAlerts(enabled)) return false;
                thresholds.SetAlertsEnabled(enabled); return true;
            });
        var usageArea = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 8 };
        usageArea.Add(capacityToggle); usageArea.Add(bar, 1); usage = usageArea;
        var resetText = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { countdown, exactReset } };
        reset = new HorizontalStackLayout { Spacing = 12, VerticalOptions = LayoutOptions.Center, Children = { resetIcon, resetText } };
        var deadlineTap = new TapGestureRecognizer();
        deadlineTap.Tapped += (_, _) => { if (deadline.IsExpiry) openReserve(); else open(row); };
        reset.GestureRecognizers.Add(deadlineTap);
        var quotaTap = new TapGestureRecognizer();
        quotaTap.Tapped += (_, _) => open(row);
        quota.GestureRecognizers.Add(quotaTap);
        Content = layout;
        UpdateClock(DateTimeOffset.Now);
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
        alertSettings = settings;
        UpdateClock(DateTimeOffset.Now);
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
            layout.Add(reset, 2);
        }
        else
        {
            layout.ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)];
            layout.RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)];
            layout.Add(quota, 0, 0);
            layout.Add(reset, 1, 0);
            layout.Add(usage, 0, 1);
            Grid.SetColumnSpan(quota, reset.IsVisible ? 1 : 2);
            Grid.SetColumnSpan(usage, 2);
        }
    }
}
