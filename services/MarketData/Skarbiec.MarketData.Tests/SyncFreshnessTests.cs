using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests;

public sealed class SyncFreshnessTests
{
    private const string WeekdayCron = "0 30 18 ? * MON-FRI";

    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(10);

    // The cron fires in the machine's local zone, so the instants are built in it too.
    private static DateTimeOffset Local(int month, int day, int hour, int minute) =>
        new(new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Local));

    [Fact]
    public void RunCompletedAfterLastSlot_OnMondayMorning_IsFresh()
    {
        var friday = Local(11, 6, 18, 35);
        var monday = Local(11, 9, 9, 0);

        Assert.False(SyncFreshness.IsDue(WeekdayCron, monday, friday, friday, RetryInterval));
    }

    [Fact]
    public void RunCompletedBeforeLastSlot_IsDue()
    {
        var fridayBeforeClose = Local(11, 6, 18, 0);
        var monday = Local(11, 9, 9, 0);

        Assert.True(SyncFreshness.IsDue(WeekdayCron, monday, fridayBeforeClose, fridayBeforeClose, RetryInterval));
    }

    [Fact]
    public void NeverCompleted_IsDue()
    {
        Assert.True(SyncFreshness.IsDue(WeekdayCron, Local(11, 9, 9, 0), null, null, RetryInterval));
    }

    [Fact]
    public void LatestRunWithinRetryInterval_IsNotDue()
    {
        var now = Local(11, 9, 9, 0);

        Assert.False(SyncFreshness.IsDue(WeekdayCron, now, null, now.AddMinutes(-2), RetryInterval));
    }

    [Fact]
    public void LastFireTime_OnMondayMorning_IsFridayClose()
    {
        Assert.Equal(Local(11, 6, 18, 30), SyncFreshness.LastFireTime(WeekdayCron, Local(11, 9, 9, 0)));
    }
}
