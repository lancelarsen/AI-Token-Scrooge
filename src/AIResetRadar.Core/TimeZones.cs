namespace AIResetRadar.Core;

public static class TimeZones
{
    public const string DefaultId = "Central Standard Time";

    public static TimeZoneInfo Resolve(string? id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(id) ? DefaultId : id); }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException)
        { return TimeZoneInfo.FindSystemTimeZoneById(DefaultId); }
    }

    public static string Format(DateTimeOffset? value, TimeZoneInfo zone, string format = "ddd, MMM d h:mm tt") =>
        value is { } at ? TimeZoneInfo.ConvertTime(at, zone).ToString(format) : "Not supplied by provider";

    public static string Abbreviation(DateTimeOffset value, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(value, zone);
        return zone.Id is "Central Standard Time" or "America/Chicago"
            ? (zone.IsDaylightSavingTime(value) ? "CDT" : "CST") : $"UTC{local:zzz}";
    }
}
