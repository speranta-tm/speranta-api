namespace Speranta.Api.Setup;

/// <summary>"This month" means the church's month in Timișoara, not UTC's.</summary>
public static class LocalTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Bucharest");

    public static DateTimeOffset StartOfMonth(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Zone);
        var first = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(first, Zone.GetUtcOffset(first)).ToUniversalTime();
    }
}
