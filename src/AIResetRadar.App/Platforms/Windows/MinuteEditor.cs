using AIResetRadar.App.Controls;
using AIResetRadar.App.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;
using Canvas = Microsoft.UI.Xaml.Controls.Canvas;
using Rectangle = Microsoft.UI.Xaml.Shapes.Rectangle;

namespace AIResetRadar.App.Platforms.Windows;

// Draw the markers and snapped thumb from the same scale, rather than rounding only the label.
public sealed class MinuteEditor : Canvas
{
    readonly MinuteSlider view;
    readonly Rectangle rail = new() { Height = 4, RadiusX = 2, RadiusY = 2, Fill = Brush(65, 67, 79), IsHitTestVisible = false };
    readonly Rectangle fill = new() { Height = 4, RadiusX = 2, RadiusY = 2, Fill = Brush(169, 144, 237), IsHitTestVisible = false };
    readonly Rectangle[] ticks;
    readonly Thumb thumb;
    double dragPosition;
    static SolidColorBrush Brush(byte r, byte g, byte b) => new(global::Windows.UI.Color.FromArgb(255, r, g, b));

    public MinuteEditor(MinuteSlider view)
    {
        this.view = view;
        Height = 44; Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Children.Add(rail); Children.Add(fill);
        ticks = Enumerable.Range(1, 30).Select(_ => new Rectangle
        { Width = 1, Height = 12, Fill = Brush(169, 144, 237), IsHitTestVisible = false }).ToArray();
        foreach (var tick in ticks) Children.Add(tick);
        thumb = new Thumb()
        {
            Width = 28, Height = 44, IsTabStop = true, UseSystemFocusVisuals = true,
            Template = (Microsoft.UI.Xaml.Controls.ControlTemplate)XamlReader.Load("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Thumb">
                    <Grid Background="Transparent">
                        <Ellipse Width="20" Height="20" Fill="#C3ADF8" Stroke="#41434F" StrokeThickness="5"
                            HorizontalAlignment="Center" VerticalAlignment="Center" />
                    </Grid>
                </ControlTemplate>
                """)
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(thumb, "Polling Interval in Minutes, 1 to 30");
        thumb.DragStarted += (_, _) => dragPosition = MinuteSliderLayout.Anchor(view.Value, ActualWidth);
        thumb.DragDelta += (_, args) =>
        {
            dragPosition += args.HorizontalChange;
            SetMinute(MinuteSliderLayout.MinuteAt(dragPosition, ActualWidth));
        };
        thumb.KeyDown += (_, args) =>
        {
            var value = args.Key switch
            {
                VirtualKey.Left or VirtualKey.Down => view.Value - 1,
                VirtualKey.Right or VirtualKey.Up => view.Value + 1,
                VirtualKey.Home => 1, VirtualKey.End => 30, _ => view.Value
            };
            if (args.Key is not (VirtualKey.Left or VirtualKey.Down or VirtualKey.Right or VirtualKey.Up or VirtualKey.Home or VirtualKey.End)) return;
            args.Handled = true; SetMinute(value);
        };
        PointerPressed += (_, args) =>
        {
            if (thumb.IsDragging) return;
            SetMinute(MinuteSliderLayout.MinuteAt(args.GetCurrentPoint(this).Position.X, ActualWidth));
            thumb.Focus(FocusState.Pointer);
        };
        Children.Add(thumb);
        SizeChanged += (_, _) => Draw();
        Loaded += (_, _) => Draw();
    }
    void SetMinute(int value)
    {
        view.SetValue(value);
        Draw();
    }
    void Draw()
    {
        var x = MinuteSliderLayout.Anchor(view.Value, ActualWidth);
        rail.Width = Math.Max(0, ActualWidth - 28); fill.Width = Math.Max(0, x - 14);
        SetLeft(rail, 14); SetTop(rail, 20); SetLeft(fill, 14); SetTop(fill, 20);
        for (var i = 0; i < ticks.Length; i++)
        {
            SetLeft(ticks[i], MinuteSliderLayout.Anchor(i + 1, ActualWidth) - 0.5); SetTop(ticks[i], 16);
        }
        SetLeft(thumb, x - 14); SetTop(thumb, 0);
    }
}
