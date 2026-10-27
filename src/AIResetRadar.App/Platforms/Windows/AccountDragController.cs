using AIResetRadar.App.Presentation;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using NativeElement = Microsoft.UI.Xaml.FrameworkElement;
using NativeScrollViewer = Microsoft.UI.Xaml.Controls.ScrollViewer;
using RootPoint = Windows.Foundation.Point;

namespace AIResetRadar.App.Platforms.Windows;

// Pointer capture stays on the grip while the real card floats above its siblings.
public sealed class AccountDragController : IDisposable
{
    readonly NativeElement grip;
    readonly string accountId;
    readonly Func<IReadOnlyList<(string Id, Border Card)>> getCards;
    readonly NativeScrollViewer scroll;
    readonly double spacing;
    readonly Func<bool> canStart;
    readonly Action<bool> setActive;
    readonly Func<int, Task> commit;
    readonly DispatcherQueueTimer timer;
    readonly KeyEventHandler keyHandler;
    (string Id, Border Card)[] cards = [];
    AccountDragLayout? layout;
    NativeElement? root;
    RootPoint origin, pointer;
    double firstTop, sourceTop, startScroll;
    bool active, moving, finishing, disposed;
    Shadow? oldShadow;
    Brush? oldStroke;
    double oldOpacity, oldStrokeWidth;
    int oldZIndex;

    public AccountDragController(NativeElement grip, string accountId, Func<IReadOnlyList<(string Id, Border Card)>> getCards,
        NativeScrollViewer scroll, double spacing, Func<bool> canStart, Action<bool> setActive, Func<int, Task> commit)
    {
        this.grip = grip; this.accountId = accountId; this.getCards = getCards; this.scroll = scroll;
        this.spacing = spacing; this.canStart = canStart; this.setActive = setActive; this.commit = commit;
        timer = grip.DispatcherQueue.CreateTimer(); timer.Interval = TimeSpan.FromMilliseconds(16);
        timer.Tick += AutoScroll;
        keyHandler = OnKeyDown;
        grip.PointerPressed += OnPressed; grip.PointerMoved += OnMoved; grip.PointerReleased += OnReleased;
        grip.PointerCanceled += OnCanceled; grip.PointerCaptureLost += OnCanceled; grip.Unloaded += OnUnloaded;
    }

    Border Source => cards[layout!.SourceIndex].Card;

    void OnPressed(object sender, PointerRoutedEventArgs args)
    {
        var point = args.GetCurrentPoint(null);
        if (disposed || active || !canStart() || !point.Properties.IsLeftButtonPressed) return;
        cards = getCards().Where(item => item.Card.Handler?.PlatformView is NativeElement element && element.ActualHeight > 0).ToArray();
        var index = Array.FindIndex(cards, item => item.Id == accountId);
        if (index < 0 || !grip.CapturePointer(args.Pointer)) return;
        layout = new(cards.Select(item => item.Card.Height).ToArray(), index, spacing);
        origin = pointer = point.Position; startScroll = scroll.VerticalOffset;
        firstTop = Top(cards[0].Card); sourceTop = Top(Source);
        oldOpacity = Source.Opacity; oldShadow = Source.Shadow; oldStroke = Source.Stroke;
        oldZIndex = Microsoft.UI.Xaml.Controls.Canvas.GetZIndex((NativeElement)Source.Handler!.PlatformView!);
        oldStrokeWidth = Source.StrokeThickness;
        active = true; moving = false; finishing = false;
        root = grip.XamlRoot.Content as NativeElement;
        root?.AddHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent, keyHandler, true);
        if (root is not null) root.SizeChanged += OnSizeChanged;
        scroll.ViewChanged += OnScrollChanged;
        setActive(true); args.Handled = true;
    }

    void OnMoved(object sender, PointerRoutedEventArgs args)
    {
        if (!active || finishing) return;
        pointer = args.GetCurrentPoint(null).Position;
        if (!moving && Math.Abs(pointer.Y - origin.Y) + Math.Abs(pointer.X - origin.X) < 5) return;
        if (!moving)
        {
            moving = true;
            // MAUI ZIndex reparents native children. Native ZIndex preserves capture and Loaded/Unloaded lifetime.
            Microsoft.UI.Xaml.Controls.Canvas.SetZIndex((NativeElement)Source.Handler!.PlatformView!, 10000);
            Source.Opacity = .94;
            Source.Stroke = Color.FromArgb("#B5A0F4"); Source.StrokeThickness = 2;
            Source.Shadow = new Shadow { Brush = Brush.Black, Offset = new Point(0, 8), Radius = 16, Opacity = .35f };
            timer.Start();
        }
        Update(); args.Handled = true;
    }

    void Update()
    {
        if (!active || !moving || finishing || layout is null) return;
        var scrollDelta = scroll.VerticalOffset - startScroll;
        // Reordering is vertical: preserve the original grab offset and keep the card in its column.
        Source.TranslationY = pointer.Y - origin.Y + scrollDelta;
        if (!InsideViewport()) return;
        var center = sourceTop + pointer.Y - origin.Y + Source.Height / 2;
        if (!layout.Move(center - firstTop + scrollDelta)) return;
        for (var i = 0; i < cards.Length; i++)
            if (i != layout.SourceIndex)
                _ = cards[i].Card.TranslateToAsync(0, layout.Tops[i] - layout.OriginalTops[i], 110, Easing.CubicOut);
    }

    void AutoScroll(DispatcherQueueTimer sender, object args)
    {
        if (!active || !moving || finishing || !InsideViewport()) return;
        var top = scroll.TransformToVisual(null).TransformPoint(default).Y;
        var step = AccountDragLayout.ScrollStep(pointer.Y, top, top + scroll.ActualHeight);
        if (step != 0) scroll.ChangeView(null, Math.Clamp(scroll.VerticalOffset + step, 0, scroll.ScrollableHeight), null, true);
    }

    bool InsideViewport()
    {
        var corner = scroll.TransformToVisual(null).TransformPoint(default);
        return pointer.X >= corner.X && pointer.X <= corner.X + scroll.ActualWidth &&
            pointer.Y >= corner.Y && pointer.Y <= corner.Y + scroll.ActualHeight;
    }

    async void OnReleased(object sender, PointerRoutedEventArgs args)
    {
        if (!active || finishing) return;
        pointer = args.GetCurrentPoint(null).Position; Update();
        args.Handled = true;
        await FinishAsync(moving && InsideViewport());
    }
    void OnCanceled(object sender, PointerRoutedEventArgs args) { if (active && !finishing) _ = FinishAsync(false); }
    void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (active && !finishing && args.Key == global::Windows.System.VirtualKey.Escape)
        { args.Handled = true; _ = FinishAsync(false); }
    }
    void OnSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs args) { if (active && !finishing) _ = FinishAsync(false); }
    void OnScrollChanged(object? sender, Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs args) => Update();

    async Task FinishAsync(bool save)
    {
        if (!active || finishing || layout is null) return;
        finishing = true; timer.Stop(); grip.ReleasePointerCaptures();
        var slot = layout.Slot;
        try
        {
            if (moving)
            {
                if (!save)
                {
                    scroll.ChangeView(null, startScroll, null, true);
                    foreach (var item in cards.Where((_, i) => i != layout.SourceIndex))
                        _ = item.Card.TranslateToAsync(0, 0, 140, Easing.CubicOut);
                }
                var target = save ? layout.Tops[layout.SourceIndex] - layout.OriginalTops[layout.SourceIndex] : 0;
                await Source.TranslateToAsync(0, target, 140, Easing.CubicOut);
            }
            if (!disposed && save && slot != layout.SourceIndex) await commit(slot);
        }
        finally { Restore(); }
    }

    void Restore()
    {
        if (!active) return;
        active = false; moving = false;
        timer.Stop();
        foreach (var item in cards) { item.Card.CancelAnimations(); item.Card.TranslationY = 0; }
        Source.Opacity = oldOpacity;
        if (Source.Handler?.PlatformView is NativeElement native) Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(native, oldZIndex);
        Source.Shadow = oldShadow!;
        Source.Stroke = oldStroke!; Source.StrokeThickness = oldStrokeWidth;
        scroll.ViewChanged -= OnScrollChanged;
        root?.RemoveHandler(Microsoft.UI.Xaml.UIElement.KeyDownEvent, keyHandler);
        if (root is not null) root.SizeChanged -= OnSizeChanged;
        grip.ReleasePointerCaptures();
        setActive(false);
    }

    static double Top(Border card) => ((NativeElement)card.Handler!.PlatformView!).TransformToVisual(null).TransformPoint(default).Y;
    void OnUnloaded(object sender, RoutedEventArgs args) => Dispose();
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        Restore(); timer.Stop(); timer.Tick -= AutoScroll;
        grip.PointerPressed -= OnPressed; grip.PointerMoved -= OnMoved; grip.PointerReleased -= OnReleased;
        grip.PointerCanceled -= OnCanceled; grip.PointerCaptureLost -= OnCanceled; grip.Unloaded -= OnUnloaded;
    }
}
