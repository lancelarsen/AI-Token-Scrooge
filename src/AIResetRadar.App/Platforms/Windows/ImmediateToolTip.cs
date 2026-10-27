using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ToolTip = Microsoft.UI.Xaml.Controls.ToolTip;

namespace AIResetRadar.App.Platforms.Windows;

public static class ImmediateToolTip
{
    public static void Attach(View view, string text)
    {
        FrameworkElement? target = null;
        ToolTip? tip = null;

        void Show(object sender, PointerRoutedEventArgs args)
        {
            if (tip is not null) tip.IsOpen = true;
        }
        void HidePointer(object sender, PointerRoutedEventArgs args) => Hide();
        void HideRouted(object sender, RoutedEventArgs args) => Hide();
        void Hide() { if (tip is not null) tip.IsOpen = false; }

        void Connect()
        {
            if (ReferenceEquals(target, view.Handler?.PlatformView)) return;
            if (target is not null)
            {
                Hide();
                target.PointerEntered -= Show;
                target.PointerExited -= HidePointer;
                target.PointerPressed -= HidePointer;
                target.LostFocus -= HideRouted;
                target.Unloaded -= HideRouted;
                ToolTipService.SetToolTip(target, null);
            }
            target = view.Handler?.PlatformView as FrameworkElement;
            if (target is null) { tip = null; return; }
            tip = new ToolTip { Content = text };
            // Register ownership before opening; pointer entry bypasses the system's hover timer.
            ToolTipService.SetToolTip(target, tip);
            target.PointerEntered += Show;
            target.PointerExited += HidePointer;
            target.PointerPressed += HidePointer;
            target.LostFocus += HideRouted;
            target.Unloaded += HideRouted;
        }

        view.HandlerChanged += (_, _) => Connect();
        Connect();
    }
}
