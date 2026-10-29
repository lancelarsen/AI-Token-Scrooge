namespace AIResetRadar.Core;

public readonly record struct AlertThresholds(decimal Warning, decimal Critical, decimal Info = 100)
{
    public bool IsValid => Critical >= 0 && Critical < Warning && Warning <= Info && Info <= 100;
    public static decimal RemainingAt(double position, double width) => width > 0 && double.IsFinite(position) && double.IsFinite(width)
        ? (decimal)Math.Round(Math.Clamp(1 - position / width, 0, 1) * 100, MidpointRounding.AwayFromZero) : 0;
    public AlertThresholds MoveInfo(decimal remaining) => this with { Info = Math.Clamp(decimal.Round(remaining), Warning, 100) };
    public AlertThresholds Move(bool warning, decimal remaining)
    {
        var value = decimal.Round(remaining, 0, MidpointRounding.AwayFromZero);
        return warning ? this with { Warning = Math.Clamp(value, decimal.Floor(Critical) + 1, Info) }
            : this with { Critical = Math.Clamp(value, 0, decimal.Ceiling(Warning) - 1) };
    }
}
