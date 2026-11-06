using AIResetRadar.Core;

namespace AIResetRadar.App.Presentation;

internal static class ResetCountdown
{
    internal const int Size = 20;
    internal const int DateSize = 12;
    internal const string Color = "#BDA3F5";
    internal const string ExpiryColor = "#EAAA51";
    internal const int IconSize = 25;
    // Fluent leaves a larger inset around the clock than around the cycle arrow.
    internal static double IconScale(bool isExpiry) => isExpiry ? 44d / 41 : 1;
    internal static string Glyph(bool isExpiry) => isExpiry ? "\uE823" : "\uE72C";
    internal static string TextColor(bool isExpiry) => isExpiry ? ExpiryColor : Color;
    internal const string DateColor = "#BEC1D2";

    internal static (DateTimeOffset? At, bool IsExpiry) Select(QuotaRow row, DateTimeOffset now)
    {
        var refresh = row.Window.ResetsAt;
        if (UsageHistory.IsEligible(row.Account.Provider, row.Window)
            && row.Snapshot.Reserve?.EarliestExpiry(now) is { } expiry
            && (refresh is null || expiry < refresh.Value))
            return (expiry, true);
        return (refresh, false);
    }

    internal static string Label(DateTimeOffset? reset, DateTimeOffset now)
    {
        var summary = ResetTime.Summary(reset, now);
        return summary.StartsWith("Resets: ", StringComparison.Ordinal) ? summary[8..] : summary;
    }
}
