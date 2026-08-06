using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features;

internal static class AssetErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.Asset", $"Asset '{id}' was not found.");

    public static Error HasTransactions(Guid id) =>
        new("Conflict.AssetHasTransactions", $"Asset '{id}' has transactions and cannot be removed.");

    public static Error InstrumentNotFound(Guid instrumentId) =>
        new("Validation.InstrumentNotFound", $"Instrument '{instrumentId}' was not found in MarketData.");

    public static readonly Error MarketDataUnavailable =
        new("ServiceUnavailable.MarketData", "MarketData is currently unavailable — try again shortly.");
}
