using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features;

internal static class InstrumentErrors
{
    public static Error UnsupportedCustomSource(PriceSource source) =>
        new(
            "Validation.UnsupportedInstrumentSource",
            $"Custom instruments can't use source '{source}' — NBP only serves its fixed FX-table/gold endpoints (T2.3), not arbitrary tickers. Use Stooq or CoinGecko.");

    public static Error AlreadyExists(PriceSource source, string ticker) =>
        new("Conflict.InstrumentAlreadyExists", $"An instrument with source '{source}' and ticker '{ticker}' already exists.");
}
