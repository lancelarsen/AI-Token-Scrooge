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
    public MainPage()
    {
        Title = "AI Reset Radar";
        Background = new LinearGradientBrush([new(Color.FromArgb("#1B1C20"), 0), new(Color.FromArgb("#141519"), 1)], new(0, 0), new(1, 1));
        var header = new Grid { Padding = new Thickness(32, 18), ColumnSpacing = 14, RowSpacing = 8,
            ColumnDefinitions = [new(new GridLength(66)), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)], RowDefinitions = [new(GridLength.Auto), new(GridLength.Auto)] };
        var icon = new Image { Source = "radar.png", WidthRequest = 66, HeightRequest = 66 }; header.Add(icon); Grid.SetRowSpan(icon, 2);
        header.Add(new Label { Text = "AI Reset Radar", FontSize = 32, FontFamily = "Segoe UI Semibold", VerticalOptions = LayoutOptions.Center }, 1);
        header.Add(refresh, 2); header.Add(settings, 3); header.Add(freshness, 1, 1); Grid.SetColumnSpan(freshness, 3);
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
        body.Children.Clear();
        rowViews.Clear();
        freshness.Text = state.IsDemo ? "DEMO DATA · Live monitoring paused" : state.Snapshots.Count == 0 ? "Connect an account to start monitoring" : $"Updated {state.Snapshots.Values.Max(s => s.ObservedAt).ToLocalTime():h:mm tt} · {DateTimeOffset.Now:ddd, MMM d, yyyy}";
        if (retiredCredentials.Count > 0) freshness.Text += " · Previous sign-in cleanup pending";
        var heading = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 20 };
        heading.Add(new VerticalStackLayout { Spacing = 5, Children = { new Label { Text = "Reset Horizon", FontSize = 28, FontFamily = "Segoe UI Semibold" },
            new Label { Text = "Upcoming quota resets and credit expirations across your accounts.", TextColor = Color.FromArgb("#B6B8C8"), FontSize = 15 } }});
        var summary = ResetSummary.Evaluate(state.Accounts, state.Snapshots, DateTimeOffset.UtcNow);
        var reserve = MakeButton(summary.ReportedCount is not { } count ? "Available resets · Count not reported" : $"{count} Codex resets {(summary.IsLastReported ? "last reported" : "available")}", "\uE8B7"); reserve.Clicked += async (_, _) => await OpenReserveAsync();
        heading.Add(new VerticalStackLayout { Spacing = 5, Padding = new Thickness(0, 0, 0, 3), Children = { reserve, new Label { Text = summary.HasExpiredDetails ? "Expiry reached · Awaiting provider confirmation" : summary.EarliestExpiry is { } expiry ? $"Earliest known expiry {expiry.ToLocalTime():MMM d · h:mm tt}" : "Expiry details may be unavailable", FontSize = 12, TextColor = Color.FromArgb("#B6B8C8"), HorizontalTextAlignment = TextAlignment.End } }}, 1);
        body.Children.Add(heading); body.Children.Add(new HorizonView(state.Events(DateTimeOffset.Now), e => OpenAccount(e.AccountId, e.WindowId)));
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
        foreach (var row in state.Rows)
        {
            var view = new QuotaRowView(row, async r => await OpenDetailsAsync(r));
            rowViews.Add(view); body.Children.Add(view);
        }
        foreach (var account in state.Accounts.Where(a => !a.Enabled || !state.Snapshots.TryGetValue(a.Id, out var snapshot) || snapshot.Windows.Count == 0))
        {
            state.Snapshots.TryGetValue(account.Id, out var snapshot);
            var accountStatus = new Button { Text = $"{account.Provider} / {account.Nickname} · {(!account.Enabled ? "Monitoring disabled" : snapshot?.Message ?? "Waiting for usage data")} · Manage account", HeightRequest = -1, MinimumHeightRequest = 70 };
            accountStatus.Clicked += async (_, _) => await OpenSettingsAsync(); body.Children.Add(accountStatus);
        }
    }
    static Button MakeButton(string text, string glyph) => new() { Text = text, ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 9), ImageSource = glyph switch
    {
        "\uE72C" => ImageSource.FromFile("refresh.png"), "\uE713" => ImageSource.FromFile("settings.png"),
        _ => new FontImageSource { FontFamily = "Segoe Fluent Icons", Glyph = glyph, Color = Color.FromArgb("#B5A0F4"), Size = 20 }
    } };
}
