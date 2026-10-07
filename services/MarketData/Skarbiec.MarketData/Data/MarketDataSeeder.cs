using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Data;

// Illustrative starter instruments plus the currency catalog; idempotent by natural key, so safe on every startup.
public static class MarketDataSeeder
{
    // Metals carry their fixed MetalInstruments id and are priced per gram of fine metal, despite the ounce-style tickers.
    private static readonly (Guid? Id, string Ticker, string Name, PriceSource Source, string QuoteCurrency, AssetClass AssetClass)[] SeedInstruments =
    [
        (MetalInstruments.Gold, "XAU", "Gold (1 g)", PriceSource.GoldApi, "USD", AssetClass.PreciousMetal),
        (MetalInstruments.Silver, "XAG", "Silver (1 g)", PriceSource.GoldApi, "USD", AssetClass.PreciousMetal),
        (null, "CDR.WA", "CD Projekt", PriceSource.Yahoo, "PLN", AssetClass.Stock),
        (null, "VWCE.DE", "Vanguard FTSE All-World UCITS ETF (Acc)", PriceSource.Yahoo, "EUR", AssetClass.Etf),
        (null, "bitcoin", "Bitcoin", PriceSource.CoinGecko, "USD", AssetClass.Crypto),
        (null, "ethereum", "Ethereum", PriceSource.CoinGecko, "USD", AssetClass.Crypto),
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
                    Id = seed.Id ?? Guid.NewGuid(),
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
