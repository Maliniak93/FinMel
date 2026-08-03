using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.CoinGecko;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// T2.5 AC: "Live smoke once: BTC/ETH prices land". Hits the real CoinGecko API over the network —
/// deliberately <see cref="FactAttribute.Skip"/>-marked so CI (which runs <c>dotnet test</c> with no
/// filter) never depends on live internet or CoinGecko's free-tier availability/rate limit. Run
/// locally by temporarily removing <c>Skip</c> and executing:
/// <c>dotnet test --filter FullyQualifiedName~CoinGeckoLiveSmokeTests</c> against a Docker-backed
/// Testcontainers Postgres. Same pattern as T2.3's <c>NbpLiveSmokeTests</c>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class CoinGeckoLiveSmokeTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact(Skip = "Manual live smoke (T2.5 AC) — hits the real CoinGecko API; run explicitly, don't enable in CI.")]
    public async Task LiveCoinGeckoApi_BitcoinAndEthereumPrices_LandInDb_UpsertIdempotently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://api.coingecko.com/api/v3/") };
        // Without this, CoinGecko 403s every request (see CoinGeckoSourceExtensions' doc comment) —
        // mirrored here since this test builds its own HttpClient instead of going through DI.
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Skarbiec/1.0 (+https://github.com/; personal wealth-management app, MarketData service)");
        ICoinGeckoApiClient apiClient = new CoinGeckoApiClient(httpClient);
        var priceSource = new CoinGeckoPriceSource(apiClient, NullLogger<CoinGeckoPriceSource>.Instance);

        await SyncOnceAsync(priceSource, cancellationToken);
        var afterFirstRun = await CountPriceQuotesAsync(cancellationToken);

        await SyncOnceAsync(priceSource, cancellationToken);
        var afterSecondRun = await CountPriceQuotesAsync(cancellationToken);

        Assert.True(afterFirstRun > 0, "expected at least one live crypto price to land in marketdata_db");
        Assert.Equal(afterFirstRun, afterSecondRun); // re-running the same day upserts, doesn't duplicate
    }

    // Deliberately minimal, test-only upsert (check-exists-then-add-or-update, same pattern as
    // NbpLiveSmokeTests/MarketDataSeeder) — proves the source's output round-trips through the real
    // (instrument, date) unique index from T2.1. The scheduled, failure-isolated version of this
    // belongs to PriceSyncJob (T2.6).
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
