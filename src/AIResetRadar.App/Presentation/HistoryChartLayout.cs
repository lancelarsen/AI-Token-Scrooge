namespace AIResetRadar.App.Presentation;

/// <summary>Stable chart appearance independent of refresh order and process lifetime.</summary>
public static class HistoryChartLayout
{
    public static string AccountColor(AIResetRadar.Core.ProviderAccount account, string plan)
    {
        if (account.LineColor is { Length: 7 } color && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit)) return color;
        return account.Provider switch {
            "Claude" => "#58A6FF",
            "GitHub Copilot" => "#CDEB82",
            "OpenAI" => plan.Contains("Plus", StringComparison.OrdinalIgnoreCase) ? "#D587A5" : "#B99AF8",
            _ => ColorForAccount(account.Id)
        };
    }

    public static string ColorForAccount(string accountId)
    {
        // String.GetHashCode is randomized per process; a stable hash keeps account colors
        // unchanged after restart, addition/removal of another account, and custom sorting.
        uint hash = 2166136261;
        foreach (var character in accountId) hash = unchecked((hash ^ character) * 16777619);
        var hue = (hash & 0xFFFFFF) / 16777216d * 6;
        var saturation = .48 + ((hash >> 24) & 0xFF) / 255d * .22;
        const double lightness = .72;
        var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var secondary = chroma * (1 - Math.Abs(hue % 2 - 1));
        var offset = lightness - chroma / 2;
        var (r, g, b) = (int)hue switch
        {
            0 => (chroma, secondary, 0d), 1 => (secondary, chroma, 0d),
            2 => (0d, chroma, secondary), 3 => (0d, secondary, chroma),
            4 => (secondary, 0d, chroma), _ => (chroma, 0d, secondary)
        };
        return $"#{(int)Math.Round((r + offset) * 255):X2}{(int)Math.Round((g + offset) * 255):X2}{(int)Math.Round((b + offset) * 255):X2}";
    }

    public static float Height(int accounts, int futureRows) =>
        Math.Max(294, Math.Max(107 + Math.Max(0, futureRows) * 76, 70 + Math.Max(0, accounts) * 25));

    /// <summary>Places ordered labels inside the graph while preserving their vertical order.</summary>
    public static float[] PlaceLabels(IReadOnlyList<float> desired, float top, float bottom, float spacing = 25)
    {
        if (desired.Count == 0) return [];
        bottom = Math.Max(top, bottom);
        spacing = Math.Min(spacing, (bottom - top) / Math.Max(1, desired.Count - 1));
        var positions = new float[desired.Count];
        for (var i = 0; i < positions.Length; i++)
            positions[i] = Math.Max(Math.Clamp(desired[i], top, bottom), i == 0 ? top : positions[i - 1] + spacing);
        positions[^1] = Math.Min(positions[^1], bottom);
        for (var i = positions.Length - 2; i >= 0; i--)
            positions[i] = Math.Min(positions[i], positions[i + 1] - spacing);
        return positions;
    }
}
