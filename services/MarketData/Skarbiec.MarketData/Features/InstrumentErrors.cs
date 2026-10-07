using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features;

internal static class InstrumentErrors
{
    public static Error UnsupportedCustomSource(PriceSource source) =>
        new(
            "Validation.UnsupportedInstrumentSource",
            $"Custom instruments can't use source '{source}' — it serves only the built-in gold and silver instruments. Use Yahoo or CoinGecko.");

    public static Error UnsupportedAssetClass(AssetClass assetClass) =>
        new(
            "Validation.UnsupportedInstrumentAssetClass",
            $"Asset class '{assetClass}' has no market data provider — custom instruments are only for Stock, Etf or Crypto.");

    public static Error TickerNotFound(PriceSource source, string ticker) =>
        new(
            "Validation.TickerNotFound",
            $"'{ticker}' was not found at {source} — check the ticker and try again.");

    public static Error ProviderUnreachable(PriceSource source, string ticker) =>
        new(
            "ServiceUnavailable.TickerVerificationUnreachable",
            $"{source} could not be reached to verify '{ticker}' — try again shortly, or add it anyway and it will resolve automatically.");

    public static Error AlreadyExists(PriceSource source, string ticker) =>
        new("Conflict.InstrumentAlreadyExists", $"An instrument with source '{source}' and ticker '{ticker}' already exists.");

    public static Error NotFound(Guid id) =>
        new("NotFound.Instrument", $"Instrument '{id}' was not found.");
}
