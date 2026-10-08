using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.CoinGecko;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

// Skipped so CI never depends on the live API; remove Skip to run it once locally.
public sealed class CoinGeckoLiveSmokeTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact(Skip = "Manual live smoke (T2.5 AC) — hits the real CoinGecko API; run explicitly, don't enable in CI.")]
    public async Task LiveCoinGeckoApi_BitcoinAndEthereumPrices_LandInDb_UpsertIdempotently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://api.coingecko.com/api/v3/") };
        // CoinGecko answers 403 without a User-Agent, and this test builds its own HttpClient.
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Skarbiec/1.0 (+https://github.com/; personal wealth-management app, MarketData service)");
        ICoinGeckoApiClient apiClient = new CoinGeckoApiClient(httpClient);
        var priceSource = new CoinGeckoPriceSource(apiClient, NullLogger<CoinGeckoPriceSource>.Instance);

        await SyncOnceAsync(priceSource, cancellationToken);
        var afterFirstRun = await CountPriceQuotesAsync(cancellationToken);

        await SyncOnceAsync(priceSource, cancellationToken);
        var afterSecondRun = await CountPriceQuotesAsync(cancellationToken);

        Assert.True(afterFirstRun > 0, "expected at least one live crypto price to land in marketdata_db");
        Assert.Equal(afterFirstRun, afterSecondRun);
    }

    // A minimal test-only upsert proving the output round-trips through the real unique index.
    private async Task SyncOnceAsync(IPriceSource priceSource, CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();

        var instruments = new List<Instrument>();
        foreach (var (ticker, name) in new[] { ("bitcoin", "Bitcoin"), ("ethereum", "Ethereum") })
        {
            var instrument = await db.Instruments.SingleOrDefaultAsync(
                i => i.Source == PriceSource.CoinGecko && i.Ticker == ticker, cancellationToken);
            if (instrument is null)
            {
                instrument = new Instrument
                {
                    Id = Guid.NewGuid(),
                    Ticker = ticker,
                    Name = name,
                    Source = PriceSource.CoinGecko,
                    QuoteCurrency = "USD",
                    AssetClass = AssetClass.Crypto,
                };
                db.Instruments.Add(instrument);
                await db.SaveChangesAsync(cancellationToken);
            }

            instruments.Add(instrument);
        }

        var result = await priceSource.FetchLatestAsync(instruments, cancellationToken);
        Assert.Equal(PriceFetchOutcome.Success, result.Outcome);

        foreach (var quote in result.Values)
        {
            var existing = await db.PriceQuotes.SingleOrDefaultAsync(
                q => q.InstrumentId == quote.InstrumentId && q.Date == quote.Date, cancellationToken);
            if (existing is null)
            {
                db.PriceQuotes.Add(new PriceQuote { Id = Guid.NewGuid(), InstrumentId = quote.InstrumentId, Date = quote.Date, Close = quote.Close });
            }
            else
            {
                existing.Close = quote.Close;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> CountPriceQuotesAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        return await db.PriceQuotes.CountAsync(cancellationToken);
    }
}
