using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Data;

// Illustrative starter instruments plus the currency catalog; idempotent by natural key, so safe on every startup.
public static class MarketDataSeeder
{
    private static readonly (string Ticker, string Name, PriceSource Source, string QuoteCurrency, AssetClass AssetClass)[] SeedInstruments =
    [
        // NBP prices 1 gram, not a troy ounce, despite the XAU ticker.
        ("XAU", "Gold (1 gram, NBP)", PriceSource.Nbp, "PLN", AssetClass.PreciousMetal),
        ("AAPL.US", "Apple Inc.", PriceSource.Stooq, "USD", AssetClass.Stock),
        ("CDR.PL", "CD Projekt", PriceSource.Stooq, "PLN", AssetClass.Stock),
        ("bitcoin", "Bitcoin", PriceSource.CoinGecko, "USD", AssetClass.Crypto),
        ("ethereum", "Ethereum", PriceSource.CoinGecko, "USD", AssetClass.Crypto),
    ];

    // A superset of SupportedCurrencies.All: GBP and CHF are synced though no user can pick them yet.
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
