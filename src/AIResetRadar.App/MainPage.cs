using AIResetRadar.App.Controls;
using AIResetRadar.App.Presentation;
using AIResetRadar.Core;
namespace AIResetRadar.App;

public sealed partial class MainPage : ContentPage
{
    readonly DashboardState state = new();
    TimeZoneInfo DisplayZone => TimeZones.Resolve(preferences.TimeZoneId);
    readonly VerticalStackLayout body = new() { Spacing = 20, Padding = new Thickness(32, 24, 32, 32) };
    readonly Label freshness = new() { FontSize = 12, TextColor = Color.FromArgb("#B6B8C8"), HorizontalTextAlignment = TextAlignment.End };
    readonly Button refresh = MakeButton("Refresh", "\uE72C");
    readonly Button settings = MakeButton("Settings", "\uE713");
    readonly Grid root = new() { RowDefinitions = [new(GridLength.Auto), new(new GridLength(1)), new(GridLength.Star)] };
    readonly ScrollView dashboardScroll = new();
    readonly Dictionary<string, Border> notificationCards = [];
    string? highlightedAccount;
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
                new Label { Text = "Never Waste a Token! Never Miss a Reset!", FontSize = 16, HeightRequest = 26, VerticalTextAlignment = TextAlignment.Center, TextColor = Color.FromArgb("#C3ADF8") }
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
        root.Add(header); root.Add(new BoxView { Color = Color.FromArgb("#363842") }, 0, 1); dashboardScroll.Content = body; root.Add(dashboardScroll, 0, 2); Content = root;
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
        AIResetRadar.App.Services.AppPanels.Closed += () => { if (renderPending) Dispatcher.Dispatch(Render); };
        Render();
    }
    void Render()
    {
        // Refresh continues collecting data behind connection/settings pages.
        // Preserve the covered visual tree and its focus targets until we return.
        if (!dashboardVisible || AIResetRadar.App.Services.AppPanels.IsOpen || draggedAccountId is not null || thresholdEditing) { renderPending = true; return; }
        renderPending = false;
        state.Settings = preferences;
        var now = DateTimeOffset.Now;
        var signature = state.RenderSignature(now) + ":" + retiredCredentials.Count;
        UpdateFreshness();
        if (signature == renderSignature)
        {
            foreach (var row in rowViews) row.UpdateClock(now);
            return;
        }
        body.Children.Clear();
        rowViews.Clear();
        orderCards.Clear();
        notificationCards.Clear();
        body.Children.Add(new HorizonView(state.Events(DateTimeOffset.Now), e => OpenAccount(e.AccountId, e.WindowId), DisplayZone, async () => await OpenSettingsAsync(true)));
        var toolbar = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)], ColumnSpacing = 12 };
        var add = MakeButton(state.IsDemo ? "Exit demo" : "Add account", "\uE710"); add.Clicked += async (_, _) => { if (state.IsDemo) { state.ClearDemo(); initialized = false; await InitializeAsync(); } else await AddAccountAsync(); };
        var sort = new AccountSortControl(preferences.AccountSort, mode => _ = ChangeSortAsync(mode));
        if (focusSort) { focusSort = false; sort.Loaded += (_, _) => sort.FocusSelected(preferences.AccountSort); }
        toolbar.Add(sort); toolbar.Add(add, 1); body.Children.Add(toolbar);
        if (state.Accounts.Count == 0)
        {
            var demo = MakeButton("Explore with demo data", "\uE8A5"); demo.Clicked += (_, _) => { state.LoadDemo(DateTimeOffset.Now); Render(); };
            body.Children.Add(new VerticalStackLayout { Padding = new Thickness(24, 36), Spacing = 15, Children = {
                new Label { Text = "Your AI capacity, in one quiet place.", FontSize = 24, FontFamily = "Segoe UI Semibold" },
                new Label { Text = "Connect OpenAI Codex or GitHub Copilot to see usage, upcoming resets and earned-reset expirations.", TextColor = Color.FromArgb("#B6B8C8") }, demo }});
        }
        var orderedAccounts = state.OrderedAccounts.ToArray();
        foreach (var account in orderedAccounts)
        {
            var accountRows = state.Rows.Where(row => row.Account.Id == account.Id).ToArray();
            View content;
            if (accountRows.Length == 0)
            {
                state.Snapshots.TryGetValue(account.Id, out var snapshot);
                var accountStatus = new AIResetRadar.App.Controls.ActionButton { Text = $"{account.Provider} / {account.Nickname} · {(!account.Enabled ? "Monitoring disabled" : snapshot?.Message ?? "Waiting for usage data")} · Manage account", HeightRequest = -1, MinimumHeightRequest = 70 };
                accountStatus.Clicked += async (_, _) => await OpenAccountSettingsAsync(account.Id);
                content = WithAccountSettings(accountStatus, account);
            }
            else
            {
                var windows = new List<QuotaRowView>();
                foreach (var row in accountRows)
                {
                    var view = new QuotaRowView(row, r => OpenAccount(r.Account.Id, r.Window.Id),
                        preferences.CapacityAlertsEnabled(account.Id, row.Window.Id), async enabled =>
                        {
                            try { await SetCapacityAlertsAsync(account.Id, row.Window.Id, enabled); return true; }
                            catch
                            {
                                await AIResetRadar.App.Services.ThemedDialogs.ShowAsync(this, "Alert Choice Not Saved",
                                    "Scrooge could not save this alert choice. Check local folder access and try again.", "Close");
                                return false;
                            }
                        }, values => SaveThresholdsAsync(account.Id, row.Window.Id, values), ThresholdInteraction, timeZone: DisplayZone);
                    view.UpdateAlertRules(preferences);
                    rowViews.Add(view); windows.Add(view);
                }
                content = WithAccountSettings(new AccountQuotaView(accountRows[0], windows,
                    () => OpenAccount(account.Id, accountRows[0].Window.Id)), account);
            }
            var card = new Border { Content = content, Padding = 0, Stroke = Color.FromArgb("#383B46"), BackgroundColor = Color.FromArgb("#1C1E24"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 13 } };
            if (preferences.AccountSort == AccountSortMode.CustomOrder)
                card.Content = WithOrderControls(content, card, account);
            notificationCards[account.Id] = card;
            if (highlightedAccount == account.Id) { card.Stroke = Color.FromArgb("#C3ADF8"); card.StrokeThickness = 3; }
            body.Children.Add(card);
        }
        renderSignature = signature;
    }
    void UpdateFreshness()
    {
        freshness.Text = state.IsDemo ? "DEMO DATA · Live monitoring paused" : state.Snapshots.Count == 0 ? "Connect an account to start monitoring" : UpdatedLabel(state.Snapshots.Values.Max(s => s.ObservedAt), DisplayZone);
        if (retiredCredentials.Count > 0) freshness.Text += " · Previous sign-in cleanup pending";
    }
    static string UpdatedLabel(DateTimeOffset observed, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(observed, timeZone);
        var zone = TimeZones.Abbreviation(observed, timeZone);
        var day = local.Day;
        var suffix = day is 11 or 12 or 13 ? "th" : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"Updated {local:h:mm tt} {zone}\n{local:dddd, MMM} {day}{suffix}";
    }
    View WithAccountSettings(View content, AIResetRadar.Core.ProviderAccount account)
    {
        var layout = new Grid { ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)] };
        layout.Add(content);
        var gear = new AIResetRadar.App.Controls.ActionButton { Text = "Settings", IconOnly = true };
        gear.WidthRequest = 44; gear.MinimumWidthRequest = 44; gear.HeightRequest = 44; gear.Padding = 0;
        // Eight pixels of card padding plus the first 56px row's centered 44px button.
        gear.VerticalOptions = LayoutOptions.Start; gear.Margin = new Thickness(0, 14, 12, 0);
        var description = $"Settings for {account.Provider} / {account.Nickname} · {PlanLabels.Display(account.Provider, state.Snapshots.GetValueOrDefault(account.Id)?.PlanType)}";
        SemanticProperties.SetDescription(gear, description);
        AIResetRadar.App.Platforms.Windows.ImmediateToolTip.Attach(gear, description);
        gear.Clicked += async (_, _) => await OpenAccountSettingsAsync(account.Id);
        layout.Add(gear, 1);
        return layout;
    }
    static Button MakeButton(string text, string glyph) => new AIResetRadar.App.Controls.ActionButton { Text = text };
}
