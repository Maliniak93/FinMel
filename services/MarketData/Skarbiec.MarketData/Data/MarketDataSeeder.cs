using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Data;

/// <summary>
/// Starter dictionary (T2.1 scope) so the app has something to sync against day one, plus the
/// currency catalog <see cref="Sources.FxSyncJob"/> runs against (spec-04). The instruments are
/// illustrative placeholders, not the user's real holdings — replace/extend via instrument search
/// (T2.8) once real holdings are known. Writes no <see cref="FxRate"/>: FxSyncJob's first-run 12-month
/// backfill gives every catalog currency its history. Idempotent (checked against each entity's
/// natural key), safe to call on every startup.
/// </summary>
public static class MarketDataSeeder
{
    private static readonly (string Ticker, string Name, PriceSource Source, string QuoteCurrency, AssetClass AssetClass)[] SeedInstruments =
    [
        // NBP's cenyzlota endpoint (T2.3) prices 1 gram, not a troy ounce, despite the "XAU" ticker
        // convention — the name documents the actual quoted unit so it's not misread at valuation time.
        ("XAU", "Gold (1 gram, NBP)", PriceSource.Nbp, "PLN", AssetClass.PreciousMetal),
        ("AAPL.US", "Apple Inc.", PriceSource.Stooq, "USD", AssetClass.Stock),
        ("CDR.PL", "CD Projekt", PriceSource.Stooq, "PLN", AssetClass.Stock),
        ("bitcoin", "Bitcoin", PriceSource.CoinGecko, "USD", AssetClass.Crypto),
        ("ethereum", "Ethereum", PriceSource.CoinGecko, "USD", AssetClass.Crypto),
    ];

    // A superset of SupportedCurrencies.All (asserted by MarketDataSeederTests, not a constraint):
    // GBP/CHF are synced even though no user can pick them yet.
    private static readonly (string Code, string Name, string Symbol)[] SeedCurrencies =
    [
        ("PLN", "Polish Zloty", "zł"),
        ("EUR", "Euro", "€"),
        ("USD", "US Dollar", "$"),
        ("GBP", "British Pound", "£"),
        ("CHF", "Swiss Franc", "CHF"),
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
                    VerificationStatus = InstrumentVerificationStatus.Verified,
                });
            }
        }

        var existingCodes = await db.Currencies.Select(c => c.Code).ToListAsync(cancellationToken);

        for (var displayOrder = 0; displayOrder < SeedCurrencies.Length; displayOrder++)
        {
            var seed = SeedCurrencies[displayOrder];
            if (existingCodes.Contains(seed.Code))
            {
                continue;
            }

            db.Currencies.Add(new Currency
            {
                Code = seed.Code,
                Name = seed.Name,
                Symbol = seed.Symbol,
                DecimalPlaces = 2,
                DisplayOrder = displayOrder,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
