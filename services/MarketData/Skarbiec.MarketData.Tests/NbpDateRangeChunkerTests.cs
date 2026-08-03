using Skarbiec.MarketData.Sources.Nbp;

namespace Skarbiec.MarketData.Tests;

public sealed class NbpDateRangeChunkerTests
{
    [Fact]
    public void Chunk_RangeWithinLimit_ReturnsSingleChunk()
    {
        var chunks = NbpDateRangeChunker.Chunk(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), maxDaysPerChunk: 93).ToList();

        var chunk = Assert.Single(chunks);
        Assert.Equal(new DateOnly(2026, 1, 1), chunk.From);
        Assert.Equal(new DateOnly(2026, 1, 31), chunk.To);
    }

    [Fact]
    public void Chunk_RangeExceedingLimit_SplitsContiguouslyWithNoGapOrOverlap()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = from.AddDays(199); // 200-day span, limit 93 -> 3 chunks

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

    [Fact]
    public void Chunk_SingleDayRange_ReturnsOneChunkOfOneDay()
    {
        var date = new DateOnly(2026, 8, 3);

        var chunk = Assert.Single(NbpDateRangeChunker.Chunk(date, date, maxDaysPerChunk: 93));

        Assert.Equal(date, chunk.From);
        Assert.Equal(date, chunk.To);
    }

    [Fact]
    public void Chunk_RangeExactlyAtLimit_ReturnsSingleChunk()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = from.AddDays(92); // exactly 93 days inclusive

        var chunk = Assert.Single(NbpDateRangeChunker.Chunk(from, to, maxDaysPerChunk: 93));

        Assert.Equal(from, chunk.From);
        Assert.Equal(to, chunk.To);
    }
}
