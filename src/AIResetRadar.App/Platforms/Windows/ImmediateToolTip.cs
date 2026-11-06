using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ToolTip = Microsoft.UI.Xaml.Controls.ToolTip;

namespace AIResetRadar.App.Platforms.Windows;

public static class ImmediateToolTip
{
    public static void Attach(View view, string text) => Attach(view, () => new ToolTip { Content = text });

    public static void AttachReset(View view, string context, DateTimeOffset at, TimeZoneInfo zone, bool isExpiry)
    {
        Attach(view, () =>
        {
            var accent = Brush(Presentation.ResetCountdown.TextColor(isExpiry));
            var countdown = new TextBlock { FontSize = Presentation.ResetCountdown.Size,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI Semibold"),
                Foreground = accent };
            var content = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 3 };
            content.Children.Add(new TextBlock { Text = context, TextWrapping = TextWrapping.Wrap });
            var timing = new Microsoft.UI.Xaml.Controls.StackPanel {
                Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 12,
                Margin = new Microsoft.UI.Xaml.Thickness(0, 7, 0, 0) };
            var scale = Presentation.ResetCountdown.IconScale(isExpiry);
            timing.Children.Add(new FontIcon { Glyph = Presentation.ResetCountdown.Glyph(isExpiry),
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
                RenderTransformOrigin = new global::Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new Microsoft.UI.Xaml.Media.CompositeTransform { ScaleX = scale, ScaleY = scale,
                    TranslateX = Presentation.ResetCountdown.IconOffsetX(isExpiry) },
                FontSize = Presentation.ResetCountdown.IconSize, Foreground = accent,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top, Margin = new Microsoft.UI.Xaml.Thickness(0, 3, 0, 0) });
            var times = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 3 };
            times.Children.Add(countdown);
            times.Children.Add(new TextBlock { Text = Core.TimeZones.Format(at, zone),
                FontSize = Presentation.ResetCountdown.DateSize, Foreground = Brush(Presentation.ResetCountdown.DateColor) });
            timing.Children.Add(times);
            content.Children.Add(timing);
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
