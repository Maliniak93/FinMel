namespace Skarbiec.MarketData.Sources.Nbp;

// NBP's range endpoint rejects a query wider than its own limit.
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
