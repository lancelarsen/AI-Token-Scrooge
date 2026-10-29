using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using NativeBorder = Microsoft.UI.Xaml.Controls.Border;
using NativeBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using NativeControl = Microsoft.UI.Xaml.Controls.Control;
using NativePopup = Microsoft.UI.Xaml.Controls.Primitives.Popup;
using NativePoint = Windows.Foundation.Point;
using AutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;
using Visibility = Microsoft.UI.Xaml.Visibility;

namespace AIResetRadar.App.Platforms.Windows;

/// <summary>Native focus, immediate details, and keyboard/wheel navigation for the history canvas.</summary>
public static class HistoryChartInput
{
    static readonly ConditionalWeakTable<GraphicsView, Binding> bindings = new();

    public static void Attach(GraphicsView view, Func<double, double, string?> hover, Action clear,
        Func<int, string?> moveCursor, Action<double> pan, Action followToday, View? pointerSurface = null) =>
        Replace(view, new Binding(view, hover, clear, moveCursor, pan, followToday, pointerSurface));

    public static void AttachNavigator(GraphicsView view, Action<double> pan, Action followToday) =>
        Replace(view, new Binding(view, null, null, null, pan, followToday));

    static void Replace(GraphicsView view, Binding binding)
    {
        if (bindings.TryGetValue(view, out var previous)) previous.Dispose();
        bindings.Remove(view);
        bindings.Add(view, binding);
        binding.Connect();
    }

    sealed class Binding : IDisposable
    {
        static WeakReference<Binding>? popupOwner;
        readonly GraphicsView view;
        readonly View? pointerSurface;
        FrameworkElement? pointerTarget;
        readonly Func<double, double, string?>? hover;
        readonly Action? clear;
        readonly Func<int, string?>? moveCursor;
        readonly Action<double> pan;
        readonly Action followToday;
        readonly PointerEventHandler wheelHandler;
        readonly KeyEventHandler keyHandler;
        readonly NativePopup popup;
        readonly NativeBorder surface;
        readonly TextBlock heading;
        readonly TextBlock detail;
        FrameworkElement? target;
        string? displayedText;
        bool pinned;
        bool disposed;
        bool suppressFocusReadout;
        bool IsNavigator => hover is null;

        public Binding(GraphicsView view, Func<double, double, string?>? hover, Action? clear,
            Func<int, string?>? moveCursor, Action<double> pan, Action followToday, View? pointerSurface = null)
        {
            this.view = view; this.hover = hover; this.clear = clear;
            this.pointerSurface = pointerSurface;
            this.moveCursor = moveCursor; this.pan = pan; this.followToday = followToday;
            wheelHandler = OnWheel; keyHandler = OnKeyDown;
            heading = new TextBlock { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI"),
                FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Brush(242, 241, 245), TextWrapping = TextWrapping.Wrap };
            detail = new TextBlock { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI"),
                FontSize = 13, Foreground = Brush(190, 193, 210), TextWrapping = TextWrapping.Wrap };
            var content = new Microsoft.UI.Xaml.Controls.StackPanel { Spacing = 5 };
            content.Children.Add(heading); content.Children.Add(detail);
            surface = new NativeBorder { Child = content, Padding = new Microsoft.UI.Xaml.Thickness(14, 10, 14, 10),
                CornerRadius = new Microsoft.UI.Xaml.CornerRadius(12), BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
                Background = Brush(34, 35, 41), BorderBrush = Brush(101, 87, 127) };
            popup = new NativePopup { Child = surface, IsHitTestVisible = false };
            AutomationProperties.SetLiveSetting(heading, AutomationLiveSetting.Polite);
            view.HandlerChanged += OnHandlerChanged;
            if (pointerSurface is not null) pointerSurface.HandlerChanged += OnHandlerChanged;
        }

        public void Connect()
        {
            if (disposed || ReferenceEquals(target, (pointerSurface ?? view).Handler?.PlatformView) &&
                ReferenceEquals(pointerTarget, (pointerSurface ?? view).Handler?.PlatformView)) return;
            Disconnect();
            target = (pointerSurface ?? view).Handler?.PlatformView as FrameworkElement;
            if (target is null) return;
            pointerTarget = (pointerSurface ?? view).Handler?.PlatformView as FrameworkElement ?? target;
            if (target is NativeControl control)
            {
                control.IsTabStop = true;
                control.UseSystemFocusVisuals = true;
                // A null native background leaves undrawn canvas areas out of hit testing.
                control.Background = new NativeBrush(Microsoft.UI.Colors.Transparent);
            }
            AutomationProperties.SetName(target, IsNavigator ? "History Timeline Navigator" : "Usage History");
            AutomationProperties.SetHelpText(target, IsNavigator
                ? "Arrow Keys Move One Day · Page Up and Page Down Move Seven Days · Home Returns to Today"
                : "Arrow Keys Inspect Samples · Page Up and Page Down Move Seven Days · Home Returns to Today · Escape Closes Details");
            pointerTarget.PointerEntered += OnPointer;
            pointerTarget.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointer), true);
            pointerTarget.PointerExited += OnExited;
            pointerTarget.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed), true);
            target.GotFocus += OnFocus;
            target.LostFocus += OnLostFocus;
            target.Unloaded += OnUnloaded;
            target.SizeChanged += OnSizeChanged;
            target.EffectiveViewportChanged += OnViewportChanged;
            target.AddHandler(UIElement.PointerWheelChangedEvent, wheelHandler, true);
            target.AddHandler(UIElement.KeyDownEvent, keyHandler, true);
        }

        void OnHandlerChanged(object? sender, EventArgs args) => Connect();

        void OnPointer(object sender, PointerRoutedEventArgs args)
        {
            if (target is null || IsNavigator || pinned) return;
            var point = args.GetCurrentPoint(target);
            if (point.Properties.IsLeftButtonPressed) return;
            Show(hover!(point.Position.X, point.Position.Y), point.Position);
        }

        void OnPressed(object sender, PointerRoutedEventArgs args)
        {
            if (target is null || !args.GetCurrentPoint(target).Properties.IsLeftButtonPressed) return;
            suppressFocusReadout = true;
            try { if (target is NativeControl control) control.Focus(FocusState.Pointer); }
            finally { suppressFocusReadout = false; }
            if (IsNavigator) return; // Preserve the MAUI navigator's pointer capture and drag handlers.
            var point = args.GetCurrentPoint(target).Position;
            var text = hover!(point.X, point.Y);
            pinned = text is not null;
            Show(text, point, announce: true);
            args.Handled = text is not null;
        }

        void OnExited(object sender, PointerRoutedEventArgs args) { if (!pinned) Hide(); }

        void OnFocus(object sender, RoutedEventArgs args)
        {
            if (IsNavigator || suppressFocusReadout || target is null) return;
            // Rebuilding a selector can transfer focus here programmatically.
            // Only deliberate keyboard navigation should open a sample readout.
            if (target is not NativeControl { FocusState: FocusState.Keyboard }) return;
            pinned = true;
            Show(moveCursor!(0), KeyboardAnchor(), announce: true);
        }

        void OnLostFocus(object sender, RoutedEventArgs args) => Hide();
        void OnUnloaded(object sender, RoutedEventArgs args) => Hide();
        void OnSizeChanged(object sender, SizeChangedEventArgs args) => Hide();
        void OnViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args) => Hide();

        void OnWheel(object sender, PointerRoutedEventArgs args)
        {
            if (target is null || !view.IsVisible) return;
            var properties = args.GetCurrentPoint(target).Properties;
            var delta = properties.MouseWheelDelta;
            if (delta == 0) return;
            Hide();
            pan(delta / 240d * (properties.IsHorizontalMouseWheel ? 1 : -1));
            args.Handled = true;
        }

        void OnKeyDown(object sender, KeyRoutedEventArgs args)
        {
            if (target is null) return;
            switch (args.Key)
            {
                case VirtualKey.Left:
                case VirtualKey.Up:
                case VirtualKey.Right:
                case VirtualKey.Down:
                    var direction = args.Key is VirtualKey.Left or VirtualKey.Up ? -1 : 1;
                    if (IsNavigator) { Hide(); pan(direction); }
                    else { pinned = true; Show(moveCursor!(direction), KeyboardAnchor(), announce: true); }
                    break;
                case VirtualKey.PageUp: Hide(); pan(-7); break;
                case VirtualKey.PageDown: Hide(); pan(7); break;
                case VirtualKey.Home: Hide(); followToday(); break;
                case VirtualKey.Enter:
                case VirtualKey.Space:
                    if (IsNavigator) return;
                    pinned = true; Show(moveCursor!(0), KeyboardAnchor(), announce: true); break;
                case VirtualKey.Escape: Hide(); break;
                default: return;
            }
            args.Handled = true;
        }

        NativePoint KeyboardAnchor() => new((target?.ActualWidth ?? 0) / 2, 0);

        void Show(string? text, NativePoint point, bool announce = false)
        {
            if (string.IsNullOrWhiteSpace(text) || target?.XamlRoot?.Content is null || !view.IsVisible) { Hide(); return; }
            if (popupOwner is not null && popupOwner.TryGetTarget(out var other) && other != this) other.Hide();
            popupOwner = new(this);
            var changed = displayedText != text;
            if (changed)
            {
                displayedText = text;
                var split = text.IndexOf('\n');
                heading.Text = split < 0 ? text : text[..split];
                detail.Text = split < 0 ? "" : text[(split + 1)..];
                detail.Visibility = split < 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            var root = target.XamlRoot;
            if (!popup.IsOpen) popup.XamlRoot = root;
            surface.MaxWidth = Math.Min(420, Math.Max(0, root.Size.Width - 16));
            surface.Measure(new global::Windows.Foundation.Size(surface.MaxWidth, double.PositiveInfinity));
            var anchor = target.TransformToVisual(root.Content).TransformPoint(point);
            popup.HorizontalOffset = Math.Clamp(anchor.X - surface.DesiredSize.Width / 2, 8,
                Math.Max(8, root.Size.Width - surface.DesiredSize.Width - 8));
            var above = anchor.Y - surface.DesiredSize.Height - 14;
            var top = above >= 8 ? above : anchor.Y + 18;
            popup.VerticalOffset = Math.Clamp(top, 8, Math.Max(8, root.Size.Height - surface.DesiredSize.Height - 8));
            popup.IsOpen = true;
            if (announce && changed)
            {
                AutomationProperties.SetName(heading, text);
                FrameworkElementAutomationPeer.FromElement(heading)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        }

        void Hide()
        {
            popup.IsOpen = false; pinned = false; displayedText = null;
            clear?.Invoke();
        }

        void Disconnect()
        {
            Hide();
            if (target is null) return;
            if (pointerTarget is not null)
            {
                pointerTarget.PointerEntered -= OnPointer;
                pointerTarget.RemoveHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointer));
                pointerTarget.PointerExited -= OnExited;
                pointerTarget.RemoveHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPressed));
                pointerTarget = null;
            }
            target.GotFocus -= OnFocus; target.LostFocus -= OnLostFocus;
            target.Unloaded -= OnUnloaded; target.SizeChanged -= OnSizeChanged;
            target.EffectiveViewportChanged -= OnViewportChanged;
            target.RemoveHandler(UIElement.PointerWheelChangedEvent, wheelHandler);
            target.RemoveHandler(UIElement.KeyDownEvent, keyHandler);
            target = null;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            view.HandlerChanged -= OnHandlerChanged;
            if (pointerSurface is not null) pointerSurface.HandlerChanged -= OnHandlerChanged;
            Disconnect();
        }

        static NativeBrush Brush(byte red, byte green, byte blue) =>
            new(Microsoft.UI.ColorHelper.FromArgb(255, red, green, blue));
    }
}
