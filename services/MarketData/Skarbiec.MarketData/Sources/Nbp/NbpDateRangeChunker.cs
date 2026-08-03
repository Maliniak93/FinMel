namespace Skarbiec.MarketData.Sources.Nbp;

/// <summary>
/// Splits a date range into contiguous, non-overlapping chunks no longer than
/// <paramref name="maxDaysPerChunk"/> — both NBP range endpoints reject a query wider than their own
/// limit (verified live against api.nbp.pl: exchangerates/tables/A rejects &gt;93 days with "Limit of
/// 93 days has been exceeded"; cenyzlota accepts up to 367 days). Shared by
/// <see cref="NbpFxRateSource"/> and <see cref="NbpPriceSource"/> history backfill (T2.7), which use
/// different limits for their respective endpoints.
/// </summary>
public static class NbpDateRangeChunker
{
    public static IEnumerable<(DateOnly From, DateOnly To)> Chunk(DateOnly from, DateOnly to, int maxDaysPerChunk)
    {
        var chunkStart = from;
        while (chunkStart <= to)
        {
            var chunkEnd = chunkStart.AddDays(maxDaysPerChunk - 1);
            if (chunkEnd > to)
            {
                chunkEnd = to;
            }

            yield return (chunkStart, chunkEnd);
            chunkStart = chunkEnd.AddDays(1);
        }
    }
}
