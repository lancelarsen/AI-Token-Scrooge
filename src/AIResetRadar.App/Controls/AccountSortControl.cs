using AIResetRadar.Core;

namespace AIResetRadar.App.Controls;

public sealed class AccountSortControl : Border
{
    readonly Dictionary<AccountSortMode, Button> buttons = [];

    public AccountSortControl(AccountSortMode selected, Action<AccountSortMode> choose)
    {
        Stroke = Color.FromArgb("#45464D"); StrokeThickness = 1;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 };
        BackgroundColor = Color.FromArgb("#232429");
        var grid = new Grid { ColumnSpacing = 0 };
        var modes = new[] { (AccountSortMode.CustomOrder, "Custom Order"), (AccountSortMode.MostRemaining, "Most Remaining"),
            (AccountSortMode.LeastRemaining, "Least Remaining"), (AccountSortMode.NextReset, "Next Reset") };
        for (var i = 0; i < modes.Length; i++)
        {
            if (i > 0)
            {
                grid.ColumnDefinitions.Add(new(new GridLength(1)));
                grid.Add(new BoxView { Color = Color.FromArgb("#45464D") }, grid.ColumnDefinitions.Count - 1);
            }
            var (mode, title) = modes[i];
            grid.ColumnDefinitions.Add(new(GridLength.Star));
            var button = new ActionButton { Text = title, FontSize = 13, Padding = new Thickness(8, 4), CornerRadius = 0,
                BorderWidth = 0, BackgroundColor = Color.FromArgb(mode == selected ? "#483A64" : "#232429"),
                TextColor = Color.FromArgb(mode == selected ? "#F2F1F5" : "#BEC1D2") };
            SemanticProperties.SetDescription(button, $"{title}{(mode == selected ? ", selected" : "")}");
            var first = i == 0; var last = i == modes.Length - 1;
            void AlignContent()
            {
                if (button.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.Button native)
                {
                    native.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(first ? 9 : 0, last ? 9 : 0, last ? 9 : 0, first ? 9 : 0);
                    native.HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;
                    if (native.Content is Microsoft.UI.Xaml.Controls.Grid panel)
                    {
                        panel.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center;
                        panel.ColumnDefinitions[1].Width = Microsoft.UI.Xaml.GridLength.Auto;
                        if (panel.Children[1] is Microsoft.UI.Xaml.Controls.TextBlock label)
                        {
                            label.MaxWidth = Math.Max(40, button.Width - button.Padding.HorizontalThickness - 29);
                            label.TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center;
                        }
                    }
                }
            }
            button.Loaded += (_, _) => AlignContent();
            button.SizeChanged += (_, _) => AlignContent();
            button.Clicked += (_, _) => choose(mode);
            buttons[mode] = button;
            grid.Add(button, grid.ColumnDefinitions.Count - 1);
        }
        Content = grid;
    }

    public void FocusSelected(AccountSortMode selected) => buttons[selected].Focus();
}
