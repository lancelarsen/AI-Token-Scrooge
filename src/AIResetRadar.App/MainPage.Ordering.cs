using AIResetRadar.App.Controls;
using AIResetRadar.App.Services;
using AIResetRadar.Core;

namespace AIResetRadar.App;

public sealed partial class MainPage
{
    readonly List<(string Id, Border Card)> orderCards = [];
    string? draggedAccountId;
    bool orderSaving, focusSort;
    (string AccountId, int Direction)? orderFocus;

    async Task ChangeSortAsync(AccountSortMode mode)
    {
        if (orderSaving || mode == preferences.AccountSort) return;
        orderSaving = true;
        try
        {
            var order = preferences.AccountOrder;
            if (mode == AccountSortMode.CustomOrder && !(order ?? []).Any(id => state.Accounts.Any(a => a.Id == id)))
                order = state.OrderedAccounts.Select(a => a.Id).ToArray();
            focusSort = true;
            await SaveSettingsAsync(preferences with { AccountSort = mode, AccountOrder = order });
        }
        catch
        {
            focusSort = false;
            await ThemedDialogs.ShowAsync(this, "Sort Not Saved", "Scrooge could not save the account sort. Check local folder access and try again.", "Close");
        }
        finally { orderSaving = false; }
    }

    async Task MoveAccountAsync(string accountId, int targetIndex, int focusDirection = 0)
    {
        if (orderSaving || preferences.AccountSort != AccountSortMode.CustomOrder) return;
        var order = state.OrderedAccounts.Select(a => a.Id).ToArray();
        var moved = AccountOrdering.Move(order, accountId, targetIndex);
        if (order.SequenceEqual(moved)) return;
        orderSaving = true;
        try
        {
            orderFocus = focusDirection == 0 ? null : (accountId, focusDirection);
            await SaveSettingsAsync(preferences with { AccountOrder = moved });
        }
        catch
        {
            orderFocus = null;
            await ThemedDialogs.ShowAsync(this, "Order Not Saved", "Scrooge could not save this order. The previous order is still active. Check local folder access and try again.", "Close");
        }
        finally { orderSaving = false; }
    }

    View WithOrderControls(View content, Border card, ProviderAccount account, int index, int count)
    {
        var layout = new Grid { ColumnDefinitions = [new(new GridLength(68)), new(new GridLength(1)), new(GridLength.Star)] };
        layout.Add(new BoxView { Color = Color.FromArgb("#383B46"), Margin = new Thickness(0, 14) }, 1);
        layout.Add(content, 2);
        var controls = new Grid { Padding = new Thickness(6, 10), ColumnSpacing = 0,
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Star)], VerticalOptions = LayoutOptions.Center };
        var grip = new Grid { HeightRequest = 44, MinimumWidthRequest = 28, BackgroundColor = Colors.Transparent, VerticalOptions = LayoutOptions.Center };
        grip.Add(new Label { Text = "⠿", FontFamily = "Segoe UI Symbol", FontSize = 25, TextColor = Color.FromArgb("#B5A0F4"),
            HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, InputTransparent = true });
        var dragDescription = $"Drag {account.Provider} / {account.Nickname} to reorder. You can also use Move Up and Move Down.";
        SemanticProperties.SetDescription(grip, dragDescription);
        Platforms.Windows.ImmediateToolTip.Attach(grip, dragDescription);
        controls.Add(grip);
        var arrows = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };
        var up = OrderButton("\uE70E", $"Move {account.Provider} / {account.Nickname} Up");
        var down = OrderButton("\uE70D", $"Move {account.Provider} / {account.Nickname} Down");
        up.IsEnabled = index > 0; down.IsEnabled = index < count - 1;
        up.Clicked += async (_, _) => await MoveAccountAsync(account.Id, index - 1, -1);
        down.Clicked += async (_, _) => await MoveAccountAsync(account.Id, index + 1, 1);
        arrows.Children.Add(up); arrows.Children.Add(down); controls.Add(arrows, 1); layout.Add(controls);
        if (orderFocus is { } focus && focus.AccountId == account.Id)
        {
            orderFocus = null;
            var target = focus.Direction < 0 ? up : down;
            if (!target.IsEnabled) target = target == up ? down : up;
            target.Loaded += (_, _) => target.Focus();
        }

        orderCards.Add((account.Id, card));
        Microsoft.UI.Xaml.FrameworkElement? dragSource = null;
        Platforms.Windows.AccountDragController? dragController = null;
        grip.Loaded += (_, _) => {
            if (grip.Handler?.PlatformView is not Microsoft.UI.Xaml.FrameworkElement native || ReferenceEquals(dragSource, native)) return;
            if ((body.Parent as View)?.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ScrollViewer viewport) return;
            dragController?.Dispose(); dragSource = native;
            dragController = new Platforms.Windows.AccountDragController(native, account.Id, () => orderCards.ToArray(), viewport, body.Spacing,
                () => !orderSaving && draggedAccountId is null && preferences.AccountSort == AccountSortMode.CustomOrder,
                active => {
                    draggedAccountId = active ? account.Id : null;
                    // Rebuild after native capture and the animation frame have finished unwinding.
                    if (!active && renderPending) Dispatcher.Dispatch(Render);
                }, slot => MoveAccountAsync(account.Id, slot));
        };
        return layout;
    }

    static Button OrderButton(string glyph, string description)
    {
        var button = new Button { Text = glyph, FontFamily = "Segoe Fluent Icons", FontSize = 14,
            HeightRequest = 32, MinimumWidthRequest = 28, Padding = 0, CornerRadius = 6,
            BackgroundColor = Colors.Transparent, TextColor = Color.FromArgb("#B5A0F4"), BorderWidth = 0 };
        SemanticProperties.SetDescription(button, description);
        Platforms.Windows.ImmediateToolTip.Attach(button, description);
        return button;
    }
}
