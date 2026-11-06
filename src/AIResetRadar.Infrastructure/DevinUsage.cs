using System.Globalization;
using System.Text.RegularExpressions;
using AIResetRadar.Core;

namespace AIResetRadar.Infrastructure;

public static class DevinUsage
{
    public static DevinReading Parse(string subject, string[] fields, DateTimeOffset observed)
    {
        if (fields.Length != 4 || !Number(fields[0], out var used))
            throw new ProviderAccessException("Devin returned an unreadable ACU total.", SourceHealth.Unsupported);
        decimal? limit = Number(fields[1], out var allowance) ? allowance : null;
        return new(subject, used, limit, Reset(fields[2], fields[3], observed));
    }

    static bool Number(string? text, out decimal value)
    {
        value = default;
        return text is { Length: > 0 and <= 40 } && Regex.IsMatch(text, @"\A(?:\d+|\d{1,3}(?:,\d{3})+)(?:\.\d+)?\z")
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    static DateTimeOffset? Reset(string? display, string? zoneId, DateTimeOffset observed)
    {
        if (string.IsNullOrWhiteSpace(display) || string.IsNullOrWhiteSpace(zoneId) ||
            !TimeZoneInfo.TryFindSystemTimeZoneById(zoneId, out var zone)) return null;
        var local = TimeZoneInfo.ConvertTime(observed, zone);
        foreach (var year in new[] { local.Year, local.Year + 1 })
        {
            if (!DateTime.TryParseExact($"{year}/{display}", "yyyy/M/d h:mm tt", CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var time) || zone.IsInvalidTime(time) || zone.IsAmbiguousTime(time)) continue;
            var reset = new DateTimeOffset(time, zone.GetUtcOffset(time)).ToUniversalTime();
            if (reset > observed && reset <= observed.AddDays(32)) return reset;
        }
        return null;
    }
}
