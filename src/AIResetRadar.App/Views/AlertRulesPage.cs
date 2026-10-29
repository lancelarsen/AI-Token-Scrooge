using AIResetRadar.Core;
namespace AIResetRadar.App.Views;

public sealed class AlertRulesPage : ContentPage
{
    readonly VerticalStackLayout form = new() { Padding = 20, Spacing = 10, MaximumWidthRequest = 680, HorizontalOptions = LayoutOptions.Center };
    public AlertRulesPage(ProviderAccount account, IReadOnlyList<QuotaWindow> windows, RadarSettings settings, Func<IReadOnlyList<AlertRule>, Task> save)
    {
        Title = "Alert rules"; BackgroundColor = Color.FromArgb("#141519");
        var rules = settings.Rules?.ToList() ?? [];
        form.Children.Add(new Label { Text = $"{account.Provider} / {account.Nickname}", FontSize = 25, FontFamily = "Segoe UI Semibold" });
        form.Children.Add(new Label { Text = "Choose account defaults or customize one quota window. Turning off an alert leaves monitoring running.", TextColor = Color.FromArgb("#B6B8C8") });
        var scopes = new Picker { ItemsSource = new[] { "All quota windows" }.Concat(windows.Select(w => w.Name)).ToArray(), SelectedIndex = 0, TextColor = Colors.White };
        SemanticProperties.SetDescription(scopes, "Account defaults or a specific quota window"); form.Children.Add(scopes);
        var low = Field("Low capacity: percent left"); var critical = Field("Critical capacity: percent left");
        var lowEnabled = Toggle("Low capacity alerts"); var criticalEnabled = Toggle("Critical capacity alerts");
        var exhausted = Toggle("Exhausted capacity alerts"); var restored = Toggle("Confirmed restored capacity alerts");
        var expiry = Toggle("Earned reset expiry reminders (24 hours / 1 hour)");
        var status = new Label { TextColor = Color.FromArgb("#EAAA51") }; form.Children.Add(status);
        var loading = false;
        var saveGate = new SemaphoreSlim(1, 1);
        async Task AutoSaveAsync()
        {
            if (loading) return;
            decimal? lowValue = null, criticalValue = null;
            if (!string.IsNullOrWhiteSpace(low.Text)) { if (!decimal.TryParse(low.Text, out var value)) { status.Text = "Enter a percentage or leave the field blank for defaults."; return; } lowValue = value; }
            if (!string.IsNullOrWhiteSpace(critical.Text)) { if (!decimal.TryParse(critical.Text, out var value)) { status.Text = "Enter a percentage or leave the field blank for defaults."; return; } criticalValue = value; }
            var id = WindowId(); var rule = new AlertRule(account.Id, id, lowValue, criticalValue,
                lowEnabled.IsToggled, criticalEnabled.IsToggled, exhausted.IsToggled, restored.IsToggled, expiry.IsToggled);
            await saveGate.WaitAsync();
            try
            {
            var candidate = rules.Where(r => r.AccountId != account.Id || r.WindowId != id).Append(rule).ToArray();
            if (!(settings with { Rules = candidate }).HasValidThresholds) { status.Text = "Use 0 ≤ critical < low ≤ 100. Account defaults must also fit any window overrides."; return; }
            try { await save(candidate); rules = candidate.ToList(); settings = settings with { Rules = candidate }; status.Text = ""; }
            catch { status.Text = "Rules could not be saved. Check local folder access."; }
            }
            finally { saveGate.Release(); }
        }
        low.TextChanged += async (_, _) => await AutoSaveAsync();
        critical.TextChanged += async (_, _) => await AutoSaveAsync();
        foreach (var toggle in new[] { lowEnabled, criticalEnabled, exhausted, restored, expiry })
            toggle.Toggled += async (_, _) => await AutoSaveAsync();
        var inherit = new AIResetRadar.App.Controls.ActionButton { Text = "Use default rules" };
        inherit.Clicked += async (_, _) =>
        {
            await saveGate.WaitAsync();
            try
            {
            var id = WindowId(); var candidate = rules.Where(r => r.AccountId != account.Id || r.WindowId != id).ToArray();
            if (!(settings with { Rules = candidate }).HasValidThresholds) { status.Text = "A window override conflicts with these defaults. Adjust its thresholds first."; return; }
            try { await save(candidate); rules = candidate.ToList(); settings = settings with { Rules = candidate }; Load(); } catch { status.Text = "Rules could not be saved."; }
            }
            finally { saveGate.Release(); }
        };
        form.Children.Add(inherit);
        var close = new AIResetRadar.App.Controls.ActionButton { Text = "Close" }; close.Clicked += async (_, _) => { await saveGate.WaitAsync(); saveGate.Release(); if (string.IsNullOrEmpty(status.Text)) await Navigation.PopModalAsync(); }; form.Children.Add(close);
        scopes.SelectedIndexChanged += (_, _) => Load();
        Content = new ScrollView { Content = form }; Load();
        string? WindowId() => scopes.SelectedIndex <= 0 ? null : windows[scopes.SelectedIndex - 1].Id;
        void Load()
        {
            loading = true;
            var id = WindowId(); var stored = rules.FirstOrDefault(r => r.AccountId == account.Id && r.WindowId == id);
            var effective = settings.RuleFor(account.Id, id);
            var defaults = id is null ? new RadarSettings(LowThreshold: settings.LowThreshold, CriticalThreshold: settings.CriticalThreshold).RuleFor(account.Id) : settings.RuleFor(account.Id);
            low.Text = stored?.LowThreshold?.ToString() ?? ""; critical.Text = stored?.CriticalThreshold?.ToString() ?? "";
            low.Placeholder = $"Default: {defaults.LowThreshold:0.#}%"; critical.Placeholder = $"Default: {defaults.CriticalThreshold:0.#}%";
            lowEnabled.IsToggled = effective.Low; criticalEnabled.IsToggled = effective.Critical;
            exhausted.IsToggled = effective.Exhausted; restored.IsToggled = effective.Restored; expiry.IsToggled = effective.Expiry;
            ((View)expiry.Parent!).IsVisible = id is null; status.Text = ""; loading = false;
        }
    }
    Entry Field(string title) { var field = new Entry { Keyboard = Keyboard.Numeric }; SemanticProperties.SetDescription(field, title); form.Children.Add(new VerticalStackLayout { Spacing = 4, Children = { new Label { Text = title, TextColor = Color.FromArgb("#B6B8C8") }, field } }); return field; }
    Switch Toggle(string title)
    {
        var toggle = new Switch { OnColor = Color.FromArgb("#8170CF") }; SemanticProperties.SetDescription(toggle, title);
        var row = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 18 };
        row.Add(new Label { Text = title, VerticalOptions = LayoutOptions.Center }); row.Add(toggle, 1); form.Children.Add(row); return toggle;
    }
}
