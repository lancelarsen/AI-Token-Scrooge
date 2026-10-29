using Microsoft.Maui.Platform;
using Native = Microsoft.UI.Xaml;

namespace AIResetRadar.App.Services;

// Keep secondary screens inside the existing window, with the dashboard visible behind them.
internal static class AppPanels
{
    sealed record Panel(ContentPage Page, Native.Controls.Grid Layer, Native.UIElement? PreviousFocus);
    static readonly List<Panel> panels = [];
    static Native.Controls.Grid? host;
    static Native.UIElement? dashboard;
    public static bool IsOpen => panels.Count > 0;
    public static event Action? Closed;

    public static Task ShowAsync(Page owner, ContentPage page)
    {
        var window = owner.Window ?? Application.Current!.Windows[0];
        var native = (Native.Window)window.Handler!.PlatformView!;
        if (host is null)
        {
            dashboard = native.Content;
            native.Content = null;
            host = new Native.Controls.Grid();
            host.Children.Add(dashboard);
            native.Content = host;
        }
        var previousFocus = host.XamlRoot is { } xamlRoot ? Native.Input.FocusManager.GetFocusedElement(xamlRoot) as Native.UIElement : null;
        if (panels.Count > 0) panels[^1].Layer.Visibility = Native.Visibility.Collapsed;
        dashboard!.IsHitTestVisible = false;
        Native.Automation.AutomationProperties.SetAccessibilityView(dashboard, Native.Automation.Peers.AccessibilityView.Raw);
        page.Parent = owner;
        var view = page.ToPlatform(owner.Handler!.MauiContext!);
        var layer = new Native.Controls.Grid { Background = new Native.Media.SolidColorBrush(Windows.UI.Color.FromArgb(160, 0, 0, 0)) };
        var card = new Native.Controls.Border
        {
            Background = new Native.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 21, 25)),
            BorderBrush = new Native.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 101, 87, 127)),
            BorderThickness = new Native.Thickness(1), CornerRadius = new Native.CornerRadius(16),
            HorizontalAlignment = Native.HorizontalAlignment.Center, VerticalAlignment = Native.VerticalAlignment.Center,
            Child = view
        };
        void Resize() { card.Width = Math.Max(0, Math.Min(1000, host!.ActualWidth - 48)); card.Height = Math.Max(0, Math.Min(800, host!.ActualHeight - 64)); }
        layer.SizeChanged += (_, _) => Resize();
        layer.Children.Add(card);
        panels.Add(new(page, layer, previousFocus));
        host.Children.Add(layer);
        Resize();
        ((IPageController)page).SendAppearing();
        view.Loaded += (_, _) => (Native.Input.FocusManager.FindFirstFocusableElement(view) as Native.Controls.Control)?.Focus(Native.FocusState.Programmatic);
        layer.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape) { if (!page.SendBackButtonPressed()) _ = CloseAsync(page); e.Handled = true; }
            if (e.Key != Windows.System.VirtualKey.Tab) return;
            var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
            var direction = shift ? Native.Input.FocusNavigationDirection.Previous : Native.Input.FocusNavigationDirection.Next;
            if (!Native.Input.FocusManager.TryMoveFocus(direction, new Native.Input.FindNextElementOptions { SearchRoot = view }))
                ((shift ? Native.Input.FocusManager.FindLastFocusableElement(view) : Native.Input.FocusManager.FindFirstFocusableElement(view)) as Native.Controls.Control)?.Focus(Native.FocusState.Keyboard);
            e.Handled = true;
        };
        return Task.CompletedTask;
    }

    public static Task CloseAsync(Page? page = null)
    {
        if (panels.Count == 0) return Task.CompletedTask;
        var panel = panels[^1];
        if (page is not null && panel.Page != page) return Task.CompletedTask;
        ((IPageController)panel.Page).SendDisappearing();
        host!.Children.Remove(panel.Layer);
        panels.RemoveAt(panels.Count - 1);
        panel.Page.Handler?.DisconnectHandler();
        panel.Page.Parent = null;
        if (panels.Count > 0) panels[^1].Layer.Visibility = Native.Visibility.Visible;
        else
        {
            dashboard!.IsHitTestVisible = true;
            Native.Automation.AutomationProperties.SetAccessibilityView(dashboard, Native.Automation.Peers.AccessibilityView.Content);
            Closed?.Invoke();
        }
        if (panel.PreviousFocus is Native.Controls.Control control) control.Focus(Native.FocusState.Programmatic);
        return Task.CompletedTask;
    }
}
