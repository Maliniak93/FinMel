namespace Skarbiec.Portfolio.Features.Deposits;

public static class WarsawCalendar
{
    // InvariantGlobalization leaves Windows without ICU, so it needs its registry id; Linux resolves the IANA id from tzdata.
    private static readonly TimeZoneInfo Warsaw =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var iana)
            ? iana
            : TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

    public static DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Warsaw).DateTime);
}
