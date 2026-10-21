using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features;

internal static class InstrumentErrors
{
    public static Error UnsupportedCustomMetal() =>
        new(
            "Validation.UnsupportedInstrumentSource",
            "Gold and silver are built-in instruments — custom metal instruments can't be added.");

    public static Error UnsupportedAssetClass(AssetClass assetClass) =>
        new(
            "Validation.UnsupportedInstrumentAssetClass",
            $"Asset class '{assetClass}' has no market data provider — custom instruments are only for Stock or Etf.");

    public static Error TickerNotFound(PriceSource source, string ticker) =>
        new(
            "Validation.TickerNotFound",
            $"'{ticker}' was not found at {source} — check the ticker and try again.");

    public static Error ProviderUnreachable(PriceSource source, string ticker) =>
        new(
            "ServiceUnavailable.TickerVerificationUnreachable",
            $"{source} could not be reached to verify '{ticker}' — try again shortly, or add it anyway and it will resolve automatically.");

    public static Error UnsupportedExchange(string ticker) =>
        new(
            "Validation.UnsupportedExchange",
            $"'{ticker}' is not listed on a supported exchange — use a ticker with a configured suffix such as .WA (GPW) or .DE (Xetra).");

    public static Error NotFound(Guid id) =>
        new("NotFound.Instrument", $"Instrument '{id}' was not found.");
}
