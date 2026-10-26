using AIResetRadar.App.Controls;
using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
namespace AIResetRadar.App;

public sealed partial class MainPage : ContentPage
{
    readonly DashboardState state = new();
    readonly VerticalStackLayout body = new() { Spacing = 20, Padding = new Thickness(32, 24, 32, 32) };
    readonly Label freshness = new() { FontSize = 12, TextColor = Color.FromArgb("#B6B8C8"), HorizontalTextAlignment = TextAlignment.End };
    readonly Button refresh = MakeButton("Refresh", "\uE72C");
    readonly Button settings = MakeButton("Settings", "\uE713");
    readonly Grid root = new() { RowDefinitions = [new(GridLength.Auto), new(new GridLength(1)), new(GridLength.Star)] };
    readonly List<QuotaRowView> rowViews = [];
    bool dashboardVisible = true;
    bool renderPending;
    string? renderSignature;
    public MainPage()
    {
        Title = "AI Token Scrooge";
        Background = new LinearGradientBrush([new(Color.FromArgb("#1B1C20"), 0), new(Color.FromArgb("#141519"), 1)], new(0, 0), new(1, 1));
        var header = new Grid { Padding = new Thickness(32, 18), ColumnSpacing = 24, RowSpacing = 12,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)] };
        var brand = new HorizontalStackLayout { Spacing = 14, VerticalOptions = LayoutOptions.Center, Children = {
            new Image { Source = "token_baron.png", WidthRequest = 66, HeightRequest = 66 },
            new VerticalStackLayout { Spacing = 0, HeightRequest = 66, VerticalOptions = LayoutOptions.Center, Children = {
                new Label { Text = "AI Token Scrooge", FontSize = 32, FontFamily = "Segoe UI Semibold", HeightRequest = 40, VerticalTextAlignment = TextAlignment.Center },
                new Label { Text = "Never Waste a Token!", FontSize = 16, HeightRequest = 26, VerticalTextAlignment = TextAlignment.Center, TextColor = Color.FromArgb("#C3ADF8") }
            } }
        } };
        freshness.VerticalOptions = LayoutOptions.Center;
        var actions = new Grid { ColumnSpacing = 14, VerticalOptions = LayoutOptions.Center,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)] };
        actions.Add(freshness); actions.Add(refresh, 1); actions.Add(settings, 2);
        refresh.VerticalOptions = settings.VerticalOptions = LayoutOptions.Center;
        header.Add(brand); header.Add(actions, 1);
        bool? headerWide = null;
        SizeChanged += (_, _) =>
        {
            var wide = Width >= 1100;
            if (headerWide == wide) return;
            headerWide = wide;
            header.RowSpacing = wide ? 0 : 12;
            actions.WidthRequest = wide ? 500 : -1;
            Grid.SetColumnSpan(brand, wide ? 1 : 2);
            Grid.SetColumn(actions, wide ? 1 : 0); Grid.SetRow(actions, wide ? 0 : 1);
            Grid.SetColumnSpan(actions, wide ? 1 : 2);
        };
        root.Add(header); root.Add(new BoxView { Color = Color.FromArgb("#363842") }, 0, 1); root.Add(new ScrollView { Content = body }, 0, 2); Content = root;
        refresh.Clicked += async (_, _) => await RefreshAsync(); settings.Clicked += async (_, _) => await OpenSettingsAsync();
        if (Environment.GetCommandLineArgs().Contains("--demo")) state.LoadDemo(DateTimeOffset.Now);
        var args = Environment.GetCommandLineArgs(); var scenario = Array.IndexOf(args, "--scenario");
        if (scenario >= 0 && scenario + 1 < args.Length) state.ApplyDemoScenario(args[scenario + 1]);
        Appearing += async (_, _) =>
        {
            dashboardVisible = true;
            // Modal removal may still be completing during Appearing.
            if (renderPending) Dispatcher.Dispatch(Render);
            await InitializeAsync();
        };
        Disappearing += (_, _) => dashboardVisible = false;
        Render();
    }
    void Render()
    {
        // Refresh continues collecting data behind connection/settings pages.
        // Preserve the covered visual tree and its focus targets until we return.
        if (!dashboardVisible || Navigation.ModalStack.Count > 0) { renderPending = true; return; }
        renderPending = false;
        state.Settings = preferences;
        var now = DateTimeOffset.Now;
        var signature = state.RenderSignature(now) + ":" + retiredCredentials.Count;
        freshness.Text = state.IsDemo ? "DEMO DATA · Live monitoring paused" : state.Snapshots.Count == 0 ? "Connect an account to start monitoring" : UpdatedLabel(state.Snapshots.Values.Max(s => s.ObservedAt));
        if (retiredCredentials.Count > 0) freshness.Text += " · Previous sign-in cleanup pending";
        if (signature == renderSignature)
        {
            foreach (var row in rowViews) row.UpdateClock(now);
            return;
        }
        body.Children.Clear();
        rowViews.Clear();
        body.Children.Add(new HorizonView(state.Events(DateTimeOffset.Now), e => OpenAccount(e.AccountId, e.WindowId)));
        var toolbar = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)], ColumnSpacing = 12 };
        toolbar.Add(new Label { Text = "Accounts", FontSize = 24, FontFamily = "Segoe UI Semibold", VerticalOptions = LayoutOptions.Center });
        var add = MakeButton(state.IsDemo ? "Exit demo" : "Add account", "\uE710"); add.Clicked += async (_, _) => { if (state.IsDemo) { state.ClearDemo(); initialized = false; await InitializeAsync(); } else await AddAccountAsync(); };
        var sort = new Picker { WidthRequest = 180, ItemsSource = new[] { "Next reset", "Least remaining", "Account" }, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#232429"), SelectedItem = state.Sort };
        SemanticProperties.SetDescription(sort, "Sort accounts by next reset, least remaining, or account");
        sort.SelectedIndexChanged += (_, _) => { if (sort.SelectedItem is string value && value != state.Sort) { state.Sort = value; Render(); } };
        toolbar.Add(add, 1); toolbar.Add(sort, 2); body.Children.Add(toolbar);
        if (state.Accounts.Count == 0)
        {
            var demo = MakeButton("Explore with demo data", "\uE8A5"); demo.Clicked += (_, _) => { state.LoadDemo(DateTimeOffset.Now); Render(); };
            body.Children.Add(new VerticalStackLayout { Padding = new Thickness(24, 36), Spacing = 15, Children = {
                new Label { Text = "Your AI capacity, in one quiet place.", FontSize = 24, FontFamily = "Segoe UI Semibold" },
                new Label { Text = "Connect OpenAI Codex or GitHub Copilot to see usage, upcoming resets and earned-reset expirations.", TextColor = Color.FromArgb("#B6B8C8") }, demo }});
        }
        foreach (var accountRows in state.Rows.GroupBy(row => row.Account.Id))
        {
            var section = new VerticalStackLayout { Spacing = 0 };
            var firstWindow = true;
            foreach (var row in accountRows)
            {
                if (!firstWindow) section.Children.Add(new BoxView { HeightRequest = 1, Margin = new Thickness(24, 0), Color = Color.FromArgb("#383B46") });
                var view = new QuotaRowView(row, r => OpenAccount(r.Account.Id, r.Window.Id), grouped: true, showAccount: firstWindow);
                rowViews.Add(view); section.Children.Add(view);
                firstWindow = false;
            }
            body.Children.Add(new Border { Content = WithAccountSettings(section, accountRows.First().Account), Padding = 0, Stroke = Color.FromArgb("#383B46"), BackgroundColor = Color.FromArgb("#1C1E24"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 13 } });
        }
        foreach (var account in state.Accounts.Where(a => !a.Enabled || !state.Snapshots.TryGetValue(a.Id, out var snapshot) || snapshot.Windows.Count == 0))
        {
            state.Snapshots.TryGetValue(account.Id, out var snapshot);
            var accountStatus = new AIResetRadar.App.Controls.ActionButton { Text = $"{account.Provider} / {account.Nickname} · {(!account.Enabled ? "Monitoring disabled" : snapshot?.Message ?? "Waiting for usage data")} · Manage account", HeightRequest = -1, MinimumHeightRequest = 70 };
            accountStatus.Clicked += async (_, _) => await OpenAccountSettingsAsync(account.Id); body.Children.Add(WithAccountSettings(accountStatus, account));
        }
        renderSignature = signature;
    }
    static string UpdatedLabel(DateTimeOffset observed)
    {
        var local = observed.ToLocalTime();
        var zone = TimeZoneInfo.Local.Id is "Central Standard Time" or "America/Chicago"
            ? (TimeZoneInfo.Local.IsDaylightSavingTime(local) ? "CDT" : "CST") : $"UTC{local:zzz}";
        var day = local.Day;
        var suffix = day is 11 or 12 or 13 ? "th" : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"Updated {local:h:mm tt} {zone}\n{local:dddd, MMM} {day}{suffix}";
    }
    View WithAccountSettings(View content, AIResetRadar.Core.ProviderAccount account)
    {
        var layout = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        layout.Add(content);
        var gear = MakeButton("Settings", "\uE713");
        gear.HeightRequest = 44;
        gear.VerticalOptions = LayoutOptions.Start; gear.Margin = new Thickness(0, 20, 12, 0);
        var description = $"Settings for {account.Provider} / {account.Nickname} · {PlanLabels.Display(account.Provider, state.Snapshots.GetValueOrDefault(account.Id)?.PlanType)}";
        SemanticProperties.SetDescription(gear, description);
        ToolTipProperties.SetText(gear, description);
        gear.Clicked += async (_, _) => await OpenAccountSettingsAsync(account.Id);
        layout.Add(gear, 1);
        return layout;
    }
    static Button MakeButton(string text, string glyph) => new AIResetRadar.App.Controls.ActionButton { Text = text };
}
