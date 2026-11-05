using AIResetRadar.Core;

namespace AIResetRadar.App.Presentation;

internal static class ResetCountdown
{
    internal const int Size = 20;
    internal const int DateSize = 12;
    internal const string Color = "#BDA3F5";
    internal const string DateColor = "#BEC1D2";

    internal static string Label(DateTimeOffset? reset, DateTimeOffset now)
    {
        var summary = ResetTime.Summary(reset, now);
        return summary.StartsWith("Resets: ", StringComparison.Ordinal) ? summary[8..] : summary;
    }
}
