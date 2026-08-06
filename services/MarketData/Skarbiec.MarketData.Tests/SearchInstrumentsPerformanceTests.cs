using System.Diagnostics;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.SearchInstruments;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// T2.8 AC: "search returns matches with last price and date in &lt;100 ms on seeded data" — measured
/// against <see cref="SearchInstrumentsHandler"/> directly (DB round trips only), not over HTTP: a full
/// Kestrel/JSON round trip would mix in overhead the AC isn't about. A warm-up call absorbs first-query
/// JIT/connection-pool cost before the timed call, mirroring how the AC reads in practice (a query
/// against a warm service, not a cold-start request).
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class SearchInstrumentsPerformanceTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task HandleAsync_OnSeededDictionary_CompletesUnder100Milliseconds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var db = CreateDbContext())
        {
            for (var i = 0; i < 50; i++)
            {
                var instrument = new Instrument
                {
                    Id = Guid.NewGuid(),
                    Ticker = $"SYM{i:D4}.US",
                    Name = $"Symbol {i:D4} Inc.",
                    Source = PriceSource.Stooq,
                    QuoteCurrency = "USD",
                    AssetClass = AssetClass.Stock,
                };
                db.Instruments.Add(instrument);
                db.PriceQuotes.Add(new PriceQuote { Id = Guid.NewGuid(), InstrumentId = instrument.Id, Date = new DateOnly(2026, 8, 3), Close = 100m + i });
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await using var queryDb = CreateDbContext();
        var handler = new SearchInstrumentsHandler(queryDb);

        await handler.HandleAsync("SYM", 20, cancellationToken); // warm-up: absorb JIT/connection-pool cost.

        var stopwatch = Stopwatch.StartNew();
        var results = await handler.HandleAsync("SYM", 20, cancellationToken);
        stopwatch.Stop();

        Assert.Equal(20, results.Count);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 100,
            $"SearchInstruments took {stopwatch.ElapsedMilliseconds} ms on 50 seeded instruments, expected < 100 ms.");
    }
}
