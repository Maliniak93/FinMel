using Skarbiec.MarketData.Sources.Nbp;

namespace Skarbiec.MarketData.Tests;

public sealed class NbpDateRangeChunkerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(93)]
    public void Chunk_RangeAtOrBelowLimit_ReturnsSingleChunk(int days)
    {
        var from = new DateOnly(2026, 1, 1);
        var to = from.AddDays(days - 1);

        var chunk = Assert.Single(NbpDateRangeChunker.Chunk(from, to, maxDaysPerChunk: 93));

        Assert.Equal(from, chunk.From);
        Assert.Equal(to, chunk.To);
    }

    [Fact]
    public void Chunk_RangeExceedingLimit_SplitsContiguouslyWithNoGapOrOverlap()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = from.AddDays(199);

        var chunks = NbpDateRangeChunker.Chunk(from, to, maxDaysPerChunk: 93).ToList();

        Assert.Equal(3, chunks.Count);
        Assert.Equal(from, chunks[0].From);
        Assert.Equal(to, chunks[^1].To);
        for (var i = 1; i < chunks.Count; i++)
        {
            Assert.Equal(chunks[i - 1].To.AddDays(1), chunks[i].From);
        }

        Assert.All(chunks, c => Assert.True(c.To.DayNumber - c.From.DayNumber + 1 <= 93));
    }
}
