using Quartz;

namespace Skarbiec.MarketData.Sources;

public static class SyncFreshness
{
    private const int SlotLookbackDays = 8;

    // Null when the cron has no slot in the lookback window, i.e. nothing can have been missed.
    public static DateTimeOffset? LastFireTime(string cron, DateTimeOffset now)
    {
        var expression = new CronExpression(cron);
        DateTimeOffset? last = null;
        var cursor = now.AddDays(-SlotLookbackDays);

        while (expression.GetNextValidTimeAfter(cursor) is { } next && next <= now)
        {
            last = next;
            cursor = next;
        }

        return last;
    }

    public static bool IsDue(
        string cron,
        DateTimeOffset now,
        DateTimeOffset? lastCompletedStart,
        DateTimeOffset? latestStart,
        TimeSpan minRetryInterval)
    {
        if (latestStart is { } latest && now - latest < minRetryInterval)
        {
            return false;
        }

        if (LastFireTime(cron, now) is not { } slot)
        {
            return false;
        }

        return lastCompletedStart is not { } completed || completed < slot;
    }
}
