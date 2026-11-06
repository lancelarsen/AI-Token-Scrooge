using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ToolTip = Microsoft.UI.Xaml.Controls.ToolTip;

namespace AIResetRadar.App.Platforms.Windows;

public static class ImmediateToolTip
{
    public static void Attach(View view, string text) => Attach(view, () => new ToolTip { Content = text });

    public static void AttachReset(View view, string context, DateTimeOffset at, TimeZoneInfo zone)
    {
        Attach(view, () =>
        {
            var countdown = new TextBlock { FontSize = Presentation.ResetCountdown.Size,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 7, 0, 0),
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Semibold"),
                Foreground = Brush(Presentation.ResetCountdown.Color) };
            var content = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 3 };
            content.Children.Add(new TextBlock { Text = context, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(countdown);
            content.Children.Add(new TextBlock { Text = Core.TimeZones.Format(at, zone),
                FontSize = Presentation.ResetCountdown.DateSize, Foreground = Brush(Presentation.ResetCountdown.DateColor) });
            var tip = new ToolTip { Content = content };
            tip.Opened += (_, _) => countdown.Text = Presentation.ResetCountdown.Label(at, DateTimeOffset.Now);
            return tip;
        });

        static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(string hex) =>
            new(Microsoft.UI.ColorHelper.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16),
                Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16)));
    }

    static void Attach(View view, Func<ToolTip> create)
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
            tip = create();
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
