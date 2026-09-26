namespace Skarbiec.Portfolio.Features.Deposits;

/// <summary>
/// "Today" for a term deposit's status: the Europe/Warsaw calendar date of the injected
/// <see cref="TimeProvider"/>'s now, so a test pins it with a fake clock (term-deposits).
/// </summary>
public static class WarsawCalendar
{
    // InvariantGlobalization is on, so Windows has no ICU to map the IANA id and needs its own
    // registry id; Linux (containers, CI) resolves the IANA id from tzdata.
    private static readonly TimeZoneInfo Warsaw =
        TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Warsaw", out var iana)
            ? iana
            : TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

    public static DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Warsaw).DateTime);
}
