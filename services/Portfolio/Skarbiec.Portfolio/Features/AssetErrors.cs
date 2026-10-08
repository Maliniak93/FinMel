using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

internal static class AssetErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.Asset", $"Asset '{id}' was not found.");

    public static Error InstrumentNotFound(Guid instrumentId) =>
        new("Validation.InstrumentNotFound", $"Instrument '{instrumentId}' was not found in MarketData.");

    public static Error InstrumentAssetClassMismatch(Guid instrumentId, AssetClass? instrumentClass, AssetClass assetClass) =>
        new(
            "Validation.InstrumentAssetClassMismatch",
            $"Instrument '{instrumentId}' is a {instrumentClass} instrument, so it can't price a {assetClass} asset.");

    public static Error InstrumentCurrencyMismatch(Guid instrumentId, string? quoteCurrency, string currency) =>
        new(
            "Validation.InstrumentCurrencyMismatch",
            $"Instrument '{instrumentId}' is quoted in {quoteCurrency}, so the asset's currency must be {quoteCurrency}, not {currency}.");

    public static readonly Error MarketDataUnavailable =
        new("ServiceUnavailable.MarketData", "MarketData is currently unavailable — try again shortly.");

    public static readonly Error CurrencyLockedByTransactions =
        new("Validation.CurrencyLockedByTransactions", "The currency can't be changed once the asset has transactions.");
}
