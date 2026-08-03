using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Data;

/// <summary>
/// Starter dictionary (T2.1 scope) so the app has something to sync against day one. Illustrative
/// placeholders, not the user's real holdings — replace/extend via instrument search (T2.8) once
/// real holdings are known. Idempotent (checked against each entity's natural key), safe to call
/// on every startup.
/// </summary>
public static class MarketDataSeeder
{
    // Fixed in the past so PriceSyncJob's (T2.6) real daily rows never collide with this bootstrap
    // row on the FxRate (Pair, Date) unique index.
    private static readonly DateOnly BootstrapDate = new(2020, 1, 1);

    private static readonly (string Ticker, string Name, PriceSource Source, string QuoteCurrency, AssetClass AssetClass)[] SeedInstruments =
    [
        ("XAU", "Gold (troy ounce)", PriceSource.Nbp, "PLN", AssetClass.PreciousMetal),
        ("AAPL.US", "Apple Inc.", PriceSource.Stooq, "USD", AssetClass.Stock),
        ("CDR.PL", "CD Projekt", PriceSource.Stooq, "PLN", AssetClass.Stock),
        ("bitcoin", "Bitcoin", PriceSource.CoinGecko, "USD", AssetClass.Crypto),
        ("ethereum", "Ethereum", PriceSource.CoinGecko, "USD", AssetClass.Crypto),
    ];

    // Bootstrap reference rates only — real rates arrive daily via PriceSyncJob (T2.6).
    private static readonly (string Pair, decimal Rate)[] SeedFxRates =
    [
        ("USDPLN", 3.65m),
        ("EURPLN", 4.25m),
        ("GBPPLN", 4.90m),
        ("CHFPLN", 4.55m),
    ];

    public static async Task SeedAsync(MarketDataDbContext db, CancellationToken cancellationToken = default)
    {
        foreach (var seed in SeedInstruments)
        {
            var exists = await db.Instruments.AnyAsync(
                i => i.Source == seed.Source && i.Ticker == seed.Ticker, cancellationToken);

            if (!exists)
            {
                db.Instruments.Add(new Instrument
                {
                    Id = Guid.NewGuid(),
                    Ticker = seed.Ticker,
                    Name = seed.Name,
                    Source = seed.Source,
                    QuoteCurrency = seed.QuoteCurrency,
                    AssetClass = seed.AssetClass,
                });
            }
        }

        foreach (var seed in SeedFxRates)
        {
            var exists = await db.FxRates.AnyAsync(
                r => r.Pair == seed.Pair && r.Date == BootstrapDate, cancellationToken);

            if (!exists)
            {
                db.FxRates.Add(new FxRate
                {
                    Id = Guid.NewGuid(),
                    Pair = seed.Pair,
                    Date = BootstrapDate,
                    Rate = seed.Rate,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
