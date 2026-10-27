using AIResetRadar.App.Controls;
using AIResetRadar.App.Services;
using AIResetRadar.Core;

namespace AIResetRadar.App;

public sealed partial class MainPage
{
    readonly List<(string Id, Border Card)> orderCards = [];
    string? draggedAccountId;
    bool orderSaving, focusSort;

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

    async Task MoveAccountAsync(string accountId, int targetIndex)
    {
        if (orderSaving || preferences.AccountSort != AccountSortMode.CustomOrder) return;
        var order = state.OrderedAccounts.Select(a => a.Id).ToArray();
        var moved = AccountOrdering.Move(order, accountId, targetIndex);
        if (order.SequenceEqual(moved)) return;
        orderSaving = true;
        try
        {
            await SaveSettingsAsync(preferences with { AccountOrder = moved });
        }
        catch
        {
            await ThemedDialogs.ShowAsync(this, "Order Not Saved", "Scrooge could not save this order. The previous order is still active. Check local folder access and try again.", "Close");
        }
        finally { orderSaving = false; }
    }

    View WithOrderControls(View content, Border card, ProviderAccount account)
    {
        var layout = new Grid { ColumnDefinitions = [new(new GridLength(56)), new(new GridLength(1)), new(GridLength.Star)] };
        layout.Add(new BoxView { Color = Color.FromArgb("#383B46"), Margin = new Thickness(0, 14) }, 1);
        layout.Add(content, 2);
        var grip = new Grid { HeightRequest = 44, MinimumWidthRequest = 28, BackgroundColor = Colors.Transparent, VerticalOptions = LayoutOptions.Center };
        grip.Add(new Label { Text = "⠿", FontFamily = "Segoe UI Symbol", FontSize = 25, TextColor = Color.FromArgb("#B5A0F4"),
            HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, InputTransparent = true });
        var dragDescription = $"Drag {account.Provider} / {account.Nickname} to reorder accounts.";
        SemanticProperties.SetDescription(grip, dragDescription);
        Platforms.Windows.ImmediateToolTip.Attach(grip, dragDescription);
        layout.Add(grip);

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
                    // Rebuild immediately after drag cleanup so the original order is never painted between drops.
                    if (!active && renderPending) Render();
                }, slot => MoveAccountAsync(account.Id, slot));
        };
        return layout;
    }

}
