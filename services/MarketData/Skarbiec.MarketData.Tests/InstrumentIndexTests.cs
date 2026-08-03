using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// Proves the unique indexes configured in <see cref="MarketDataDbContext.OnModelCreating"/>
/// (T2.1 AC: "duplicate (instrument, date) insert fails / upserts") actually constrain the
/// database, not just the C# model.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class InstrumentIndexTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task DuplicatePriceQuote_SameInstrumentAndDate_Fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var date = new DateOnly(2026, 8, 3);

        await using var context = CreateDbContext();
        context.PriceQuotes.Add(new PriceQuote { Id = Guid.NewGuid(), InstrumentId = instrumentId, Date = date, Close = 100m });
        await context.SaveChangesAsync(cancellationToken);

        context.PriceQuotes.Add(new PriceQuote { Id = Guid.NewGuid(), InstrumentId = instrumentId, Date = date, Close = 105m });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task DuplicateFxRate_SamePairAndDate_Fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var date = new DateOnly(2026, 8, 3);

        await using var context = CreateDbContext();
        context.FxRates.Add(new FxRate { Id = Guid.NewGuid(), Pair = "USDPLN", Date = date, Rate = 3.65m });
        await context.SaveChangesAsync(cancellationToken);

        context.FxRates.Add(new FxRate { Id = Guid.NewGuid(), Pair = "USDPLN", Date = date, Rate = 3.70m });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
    }

    [Fact]
    public async Task DuplicateInstrument_SameSourceAndTicker_Fails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var context = CreateDbContext();
        context.Instruments.Add(new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL.US",
            Name = "Apple Inc.",
            Source = PriceSource.Stooq,
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
        });
        await context.SaveChangesAsync(cancellationToken);

        context.Instruments.Add(new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL.US",
            Name = "Apple Inc. (duplicate)",
            Source = PriceSource.Stooq,
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
    }
}
