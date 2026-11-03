using System.Globalization;

namespace AIResetRadar.Core;

/// <summary>Calendar version. Compare numbers, never padded display strings.</summary>
public sealed record BuildVersion(int Year, int Month, int Day, int Revision) : IComparable<BuildVersion>
{
    public static bool TryParse(string? text, out BuildVersion version)
    {
        version = null!;
        if (text is null) return false;
        var parts = (text.StartsWith('v') ? text[1..] : text).Split('.');
        if (parts.Length != 4 || parts.Any(p => p.Length == 0 || p.Any(c => c is < '0' or > '9'))) return false;
        var values = new int[4];
        for (var i = 0; i < 4; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out values[i])) return false;
        if (values[0] is < 2026 or > 9999 || values[1] is < 1 or > 12 || values[2] < 1 ||
            values[2] > DateTime.DaysInMonth(values[0], values[1]) || values[3] is < 0 or > 65534) return false;
        version = new(values[0], values[1], values[2], values[3]);
        return true;
    }

    public int CompareTo(BuildVersion? other) => other is null ? 1 :
        (Year, Month, Day, Revision).CompareTo((other.Year, other.Month, other.Day, other.Revision));

    public string Numeric => $"{Year}.{Month}.{Day}.{Revision}";
    public override string ToString() => $"v{Year}.{Month:D2}.{Day}.{Revision:D3}";
}
