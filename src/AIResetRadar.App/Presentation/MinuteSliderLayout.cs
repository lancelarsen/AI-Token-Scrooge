namespace AIResetRadar.App.Presentation;

public static class MinuteSliderLayout
{
    public const double Inset = 14;
    public static double Anchor(int minute, double width) => Inset +
        (Math.Clamp(minute, 1, 30) - 1) * Math.Max(0, width - Inset * 2) / 29;
    public static int MinuteAt(double x, double width) => 1 + (int)Math.Round(
        Math.Clamp((x - Inset) / Math.Max(1, width - Inset * 2), 0, 1) * 29,
        MidpointRounding.AwayFromZero);
}
