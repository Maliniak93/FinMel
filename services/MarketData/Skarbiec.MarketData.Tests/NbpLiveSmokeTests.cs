using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.Nbp;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// T2.3 AC: "Live smoke (manually run once): today's or last trading day's rates land in
/// marketdata_db" + "re-running the same day is idempotent (upsert)". Hits the real NBP API over the
/// network — deliberately <see cref="FactAttribute.Skip"/>-marked so CI (which runs
/// <c>dotnet test</c> with no filter) never depends on live internet or NBP's availability. Run
/// locally by temporarily removing <c>Skip</c> and executing:
/// <c>dotnet test --filter FullyQualifiedName~NbpLiveSmokeTests</c> against a Docker-backed
/// Testcontainers Postgres.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class NbpLiveSmokeTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private static readonly string[] Currencies = ["USD", "EUR", "GBP", "CHF"];

    [Fact(Skip = "Manual live smoke (T2.3 AC) — hits the real NBP API; run explicitly, don't enable in CI.")]
    public async Task LiveNbpApi_TodaysRatesAndGoldPrice_LandInDb_UpsertIdempotently()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var httpClient = new HttpClient { BaseAddress = new Uri("https://api.nbp.pl/api/") };
        INbpApiClient apiClient = new NbpApiClient(httpClient);
        var fxSource = new NbpFxRateSource(apiClient);
        var priceSource = new NbpPriceSource(apiClient);

        await SyncOnceAsync(fxSource, priceSource, cancellationToken);
        var afterFirstRun = await CountRowsAsync(cancellationToken);

        await SyncOnceAsync(fxSource, priceSource, cancellationToken);
        var afterSecondRun = await CountRowsAsync(cancellationToken);

        Assert.True(afterFirstRun.FxRates > 0, "expected at least one live FX rate to land in marketdata_db");
        Assert.True(afterFirstRun.PriceQuotes > 0, "expected the live gold price to land in marketdata_db");
        Assert.Equal(afterFirstRun, afterSecondRun); // re-running the same day upserts, doesn't duplicate
    }

    // Deliberately minimal, test-only upsert (check-exists-then-add-or-update, same pattern as
    // MarketDataSeeder) — proves the source's output round-trips through the real (instrument, date)
    // / (pair, date) unique indexes from T2.1. The scheduled, failure-isolated version of this belongs
    // to PriceSyncJob (T2.6).
    private async Task SyncOnceAsync(IFxRateSource fxSource, IPriceSource priceSource, CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();

        var goldInstrument = await db.Instruments.SingleOrDefaultAsync(
            i => i.Source == PriceSource.Nbp && i.Ticker == "XAU", cancellationToken);
        if (goldInstrument is null)
        {
            goldInstrument = new Instrument
            {
                Id = Guid.NewGuid(),
                Ticker = "XAU",
                Name = "Gold (1 gram, NBP)",
                Source = PriceSource.Nbp,
                QuoteCurrency = "PLN",
                AssetClass = AssetClass.PreciousMetal,
            };
            db.Instruments.Add(goldInstrument);
            await db.SaveChangesAsync(cancellationToken);
        }

        var fxResult = await fxSource.FetchLatestAsync(Currencies, cancellationToken);
        Assert.Equal(PriceFetchOutcome.Success, fxResult.Outcome);
        foreach (var quote in fxResult.Values)
        {
            var existing = await db.FxRates.SingleOrDefaultAsync(
                r => r.Pair == quote.Pair && r.Date == quote.Date, cancellationToken);
            if (existing is null)
            {
                db.FxRates.Add(new FxRate { Id = Guid.NewGuid(), Pair = quote.Pair, Date = quote.Date, Rate = quote.Rate });
            }
            else
            {
                existing.Rate = quote.Rate;
            }
        }

        var priceResult = await priceSource.FetchLatestAsync([goldInstrument], cancellationToken);
        Assert.Equal(PriceFetchOutcome.Success, priceResult.Outcome);
        foreach (var quote in priceResult.Values)
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

    private async Task<(int FxRates, int PriceQuotes)> CountRowsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateDbContext();
        return (await db.FxRates.CountAsync(cancellationToken), await db.PriceQuotes.CountAsync(cancellationToken));
    }
}
