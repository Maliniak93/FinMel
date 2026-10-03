namespace Skarbiec.MarketData.Sources.Nbp;

// Both NBP range endpoints reject a query wider than their own limit.
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
