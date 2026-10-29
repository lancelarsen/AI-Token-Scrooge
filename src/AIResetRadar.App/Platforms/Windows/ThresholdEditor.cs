using AIResetRadar.App.Controls;
using AIResetRadar.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.System;
using NativeCanvas = Microsoft.UI.Xaml.Controls.Canvas;
using NativeControlTemplate = Microsoft.UI.Xaml.Controls.ControlTemplate;
using NativeBorder = Microsoft.UI.Xaml.Controls.Border;
using NativePopup = Microsoft.UI.Xaml.Controls.Primitives.Popup;
using NativeBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;

namespace AIResetRadar.App.Platforms.Windows;

// A generous transparent drag target surrounds each thin line; its visual never grows.
public sealed class ThresholdEditor : NativeCanvas, IDisposable
{
    sealed record Marker(int Kind, Thumb Grip, NativePopup Popup, TextBlock Readout)
    {
        public double? PopupTop { get; set; }
        public bool PointerInside { get; set; }
    }
    static WeakReference<ThresholdEditor>? popupOwner;
    readonly AlertThresholdView view;
    readonly Marker info;
    readonly Marker warning;
    readonly Marker critical;
    AlertThresholds values;
    Marker? active;
    double dragPosition;
    bool saving, disposed;
    double Span => Math.Max(0, ActualWidth - 16);

    public ThresholdEditor(AlertThresholdView view)
    {
        this.view = view; values = view.Thresholds; Height = 48;
        info = CreateMarker(0); warning = CreateMarker(1); critical = CreateMarker(2);
        SizeChanged += (_, _) => { if (active is not null && !saving) Cancel(); UpdatePositions(); };
        Unloaded += (_, _) => Cancel();
        EffectiveViewportChanged += (_, _) => { if (active is null) HidePopups(); };
        UpdatePositions();
    }

    Marker CreateMarker(int kind)
    {
        var isWarning = kind == 1;
        var color = kind == 0 ? "#58A6FF" : isWarning ? "#F2C94C" : "#FF5D68";
        var image = isWarning ? "threshold_warning.png" : "threshold_critical.png";
        var grip = new Thumb { Width = 32, Height = 48, IsTabStop = true, UseSystemFocusVisuals = true,
            Template = (NativeControlTemplate)XamlReader.Load($$"""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Thumb">
                    <Grid Width="32" Height="48" Background="Transparent">
                        <Rectangle Width="1.5" Fill="{{color}}" IsHitTestVisible="False" />
                    </Grid>
                </ControlTemplate>
                """) };
        var readout = new TextBlock { FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI"),
            FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new NativeBrush(Microsoft.UI.Colors.White) };
        var popupContent = new Microsoft.UI.Xaml.Controls.StackPanel { Orientation = Microsoft.UI.Xaml.Controls.Orientation.Horizontal, Spacing = 8 };
        if (kind == 0) popupContent.Children.Add(new FontIcon { Glyph = "\uE946", FontSize = 18, Foreground = new NativeBrush(Microsoft.UI.ColorHelper.FromArgb(255, 88, 166, 255)) });
        else popupContent.Children.Add(new Microsoft.UI.Xaml.Controls.Image { Width = 18, Height = 18,
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri($"ms-appx:///{image}")) });
        popupContent.Children.Add(readout);
        var popup = new NativePopup { IsHitTestVisible = false, Child = new NativeBorder {
            Child = popupContent, Padding = new Microsoft.UI.Xaml.Thickness(14, 10, 14, 10), CornerRadius = new Microsoft.UI.Xaml.CornerRadius(12),
            BorderThickness = new Microsoft.UI.Xaml.Thickness(1),
            Background = new NativeBrush(Microsoft.UI.ColorHelper.FromArgb(255, 34, 35, 41)),
            BorderBrush = new NativeBrush(Microsoft.UI.ColorHelper.FromArgb(255, 101, 87, 127)) } };
        var marker = new Marker(kind, grip, popup, readout);
        grip.PointerEntered += (_, _) => { marker.PointerInside = true; if (!saving) ShowValue(marker); };
        grip.PointerExited += (_, _) => { marker.PointerInside = false; if (active != marker) HidePopup(marker); };
        grip.LostFocus += (_, _) => { if (active != marker) HidePopup(marker); };
        grip.DragStarted += (_, _) =>
        {
            if (!Begin(marker)) { grip.CancelDrag(); return; }
            dragPosition = Span * (1 - (double)Value(marker) / 100);
            ShowValue(marker);
        };
        grip.DragDelta += (_, args) =>
        {
            if (active != marker || saving || Span <= 0) return;
            dragPosition += args.HorizontalChange;
            values = kind == 0 ? values.MoveInfo(AlertThresholds.RemainingAt(dragPosition, Span)) : values.Move(isWarning, AlertThresholds.RemainingAt(dragPosition, Span));
            UpdatePositions(); ShowValue(marker);
        };
        grip.DragCompleted += async (_, args) =>
        {
            if (active != marker || saving) return;
            if (args.Canceled) { Cancel(); return; }
            await CommitAsync(marker, keepPopup: false);
        };
        grip.KeyDown += (_, args) =>
        {
            if (args.Key == VirtualKey.Escape) { Cancel(); args.Handled = true; return; }
            var step = args.Key switch { VirtualKey.Left or VirtualKey.Up => 1, VirtualKey.Right or VirtualKey.Down => -1, _ => 0 };
            if (step == 0 || saving || (active is not null && active != marker)) return;
            if (active is null && !Begin(marker)) return;
            values = kind == 0 ? values.MoveInfo(Value(marker) + step) : values.Move(isWarning, Value(marker) + step);
            args.Handled = true; UpdatePositions(); ShowValue(marker);
        };
        grip.KeyUp += async (_, args) =>
        {
            if (args.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down) || active != marker || saving) return;
            args.Handled = true; await CommitAsync(marker, keepPopup: true);
        };
        Children.Add(grip);
        return marker;
    }

    decimal Value(Marker marker) => marker.Kind == 0 ? values.Info : marker.Kind == 1 ? values.Warning : values.Critical;
    bool Begin(Marker marker)
    {
        if (disposed || saving || active is not null || !view.IsVisible) return false;
        active = marker; marker.Grip.Focus(FocusState.Pointer); view.Interaction(true); return true;
    }
    async Task CommitAsync(Marker marker, bool keepPopup)
    {
        saving = true;
        try
        {
            if (values == view.Thresholds || await view.Save(values)) view.Thresholds = values;
            else values = view.Thresholds;
        }
        finally
        {
            saving = false; active = null;
            if (!disposed)
            {
                UpdatePositions();
                if (view.IsVisible && (marker.PointerInside || keepPopup && marker.Grip.FocusState != FocusState.Unfocused)) ShowValue(marker);
                else HidePopup(marker);
            }
            view.Interaction(false);
        }
    }
    public void Refresh(AlertThresholds thresholds)
    {
        if (active is not null || saving) return;
        view.Thresholds = values = thresholds; UpdatePositions();
    }
    public void Cancel()
    {
        HidePopups();
        if (active is null || saving) return;
        var marker = active; active = null; values = view.Thresholds;
        HidePopup(marker);
        if (marker.Grip.IsDragging) marker.Grip.CancelDrag();
        UpdatePositions(); view.Interaction(false);
    }
    void ShowValue(Marker marker)
    {
        if (disposed || XamlRoot?.Content is null || !view.IsVisible) return;
        if (popupOwner is not null && popupOwner.TryGetTarget(out var previous) && previous != this) previous.HidePopups();
        popupOwner = new(this);
        foreach (var other in new[] { info, warning, critical })
            if (other is not null && other != marker) HidePopup(other);
        UpdateMarker(marker);
        if (!marker.Popup.IsOpen) marker.Popup.XamlRoot = XamlRoot;
        var surface = (NativeBorder)marker.Popup.Child;
        surface.MaxWidth = Math.Max(0, XamlRoot.Size.Width - 8);
        surface.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var anchor = TransformToVisual(XamlRoot.Content).TransformPoint(new global::Windows.Foundation.Point(
            8 + Span * (1 - (double)Value(marker) / 100), 0));
        // Capture the hover height once. Dragging only changes the horizontal anchor and readout.
        marker.PopupTop ??= Math.Max(0, anchor.Y - surface.DesiredSize.Height - 2);
        marker.Popup.HorizontalOffset = Math.Clamp(anchor.X - surface.DesiredSize.Width / 2,
            4, Math.Max(4, XamlRoot.Size.Width - surface.DesiredSize.Width - 4));
        marker.Popup.VerticalOffset = marker.PopupTop.Value;
        marker.Popup.IsOpen = true;
    }
    void UpdateMarker(Marker marker)
    {
        var x = 8 + Span * (1 - (double)Value(marker) / 100);
        SetLeft(marker.Grip, x - 16);
        marker.Readout.Text = $"{(marker.Kind == 0 ? "Info" : marker.Kind == 1 ? "Warning" : "Critical")} Alert @ {Value(marker):0.#}% Left";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(marker.Grip, $"{marker.Readout.Text} · {view.Section}");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(marker.Grip, "Drag Horizontally or Use Arrow Keys to Adjust");
    }
    void UpdatePositions()
    {
        if (disposed) return;
        if (info is not null) UpdateMarker(info);
        if (warning is not null) UpdateMarker(warning);
        if (critical is not null) UpdateMarker(critical);
    }
    void HidePopups()
    {
        if (info is not null) HidePopup(info);
        if (warning is not null) HidePopup(warning);
        if (critical is not null) HidePopup(critical);
    }
    static void HidePopup(Marker marker) { marker.Popup.IsOpen = false; marker.PopupTop = null; }
    public void Dispose()
    {
        Cancel(); disposed = true;
        HidePopups();
    }
}
