namespace AIResetRadar.App.Presentation;

// Keeps the placeholder stable until the lifted card crosses a neighboring card's center.
public sealed class AccountDragLayout
{
    readonly double[] heights;
    readonly int[] remaining;
    readonly double spacing;
    public int SourceIndex { get; }
    public int Slot { get; private set; }
    public double[] OriginalTops { get; }
    public double[] Tops { get; private set; }

    public AccountDragLayout(double[] heights, int sourceIndex, double spacing)
    {
        this.heights = heights;
        this.spacing = spacing;
        SourceIndex = Slot = sourceIndex;
        remaining = Enumerable.Range(0, heights.Length).Where(i => i != sourceIndex).ToArray();
        Tops = Build(); OriginalTops = Tops.ToArray();
    }

    public bool Move(double centerY)
    {
        var previous = Slot;
        while (Slot > 0 && centerY < Tops[remaining[Slot - 1]] + heights[remaining[Slot - 1]] / 2)
        { Slot--; Tops = Build(); }
        while (Slot < remaining.Length && centerY > Tops[remaining[Slot]] + heights[remaining[Slot]] / 2)
        { Slot++; Tops = Build(); }
        return previous != Slot;
    }

    double[] Build()
    {
        var order = remaining.ToList(); order.Insert(Slot, SourceIndex);
        var tops = new double[heights.Length]; double top = 0;
        foreach (var index in order) { tops[index] = top; top += heights[index] + spacing; }
        return tops;
    }

    public static double ScrollStep(double pointerY, double top, double bottom)
    {
        if (pointerY < top || pointerY > bottom) return 0;
        var edge = Math.Min(56, (bottom - top) / 3);
        if (edge <= 0) return 0;
        if (pointerY < top + edge) return -10 * (1 - (pointerY - top) / edge);
        if (pointerY > bottom - edge) return 10 * (1 - (bottom - pointerY) / edge);
        return 0;
    }
}
