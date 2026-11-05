using Quartz;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources;

internal static class SyncJobData
{
    public const string SourceKey = "source";

    public static SyncRunSource ReadSource(IJobExecutionContext context) =>
        context.MergedJobDataMap.TryGetValue(SourceKey, out var value) && value is string text && Enum.TryParse<SyncRunSource>(text, out var source)
            ? source
            : SyncRunSource.Schedule;
}
