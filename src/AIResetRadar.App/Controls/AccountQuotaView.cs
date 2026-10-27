using AIResetRadar.App.Presentation;
namespace AIResetRadar.App.Controls;

// One shared identity and one set of column widths for all windows in an account.
public sealed class AccountQuotaView : Grid
{
    public AccountQuotaView(QuotaRow accountRow, IReadOnlyList<QuotaRowView> rows, Action openAccount)
    {
        Padding = new Thickness(20, 8, 16, 8);
        ColumnSpacing = 12; RowSpacing = 12;
        var providerText = new VerticalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center, Children = {
            new Label { Text = accountRow.Account.Provider, FontSize = 20, FontFamily = "Segoe UI Semibold" },
            new Border { Padding = new Thickness(11, 3), HorizontalOptions = LayoutOptions.Start,
                Stroke = Color.FromArgb("#51476D"), BackgroundColor = Color.FromArgb("#2C293E"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 15 },
                Content = new Label { Text = accountRow.AccountLabel, TextColor = Color.FromArgb("#C3ADF8"), FontSize = 12 } }
        }};
        var identity = new HorizontalStackLayout { Spacing = 16, VerticalOptions = LayoutOptions.Center, Children = {
            new Image { Source = accountRow.Account.Provider switch { "OpenAI" => "openai.png", "Claude" => "claude.png", _ => "github.png" },
                WidthRequest = 44, HeightRequest = 44, VerticalOptions = LayoutOptions.Center },
            providerText
        }};
        var identityArea = new Grid();
        identityArea.Add(identity);
        var details = new Button { Text = "", BackgroundColor = Colors.Transparent, BorderWidth = 0,
            HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
        SemanticProperties.SetDescription(details, $"{accountRow.Account.Provider}, {accountRow.AccountLabel}. Open details.");
        details.Clicked += (_, _) => openAccount();
        identityArea.Add(details);
        var windows = new VerticalStackLayout { Spacing = 4 };
        foreach (var row in rows) windows.Children.Add(row);
        this.Add(identityArea); this.Add(windows, 0, 1);
        bool? previousWide = null;
        void Reflow()
        {
            var wide = Width > 1050;
            if (previousWide == wide) return;
            previousWide = wide;
            ColumnDefinitions = wide ? [new(new GridLength(220)), new(GridLength.Star)] : [new(GridLength.Star)];
            RowDefinitions = wide ? [new(GridLength.Auto)] : [new(GridLength.Auto), new(GridLength.Auto)];
            Grid.SetColumn(windows, wide ? 1 : 0); Grid.SetRow(windows, wide ? 0 : 1);
            foreach (var row in rows) row.SetWideLayout(wide);
        }
        SizeChanged += (_, _) => Reflow();
        Reflow();
    }
}
