using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

internal static class AssetErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.Asset", $"Asset '{id}' was not found.");

    public static Error InstrumentNotFound(Guid instrumentId) =>
        new("Validation.InstrumentNotFound", $"Instrument '{instrumentId}' was not found in MarketData.");

    public static readonly Error MarketDataUnavailable =
        new("ServiceUnavailable.MarketData", "MarketData is currently unavailable — try again shortly.");

    /// <summary>
    /// Every transaction's PLN rate is tied to the asset's currency (ADR-026), so the currency can't
    /// change once there is one. UpdateAsset reports it as a field error on <c>Currency</c>.
    /// </summary>
    public static readonly Error CurrencyLockedByTransactions =
        new("Validation.CurrencyLockedByTransactions", "The currency can't be changed once the asset has transactions.");
}
