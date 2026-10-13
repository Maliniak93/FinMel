using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Watchlist;

internal static class WatchlistErrors
{
    public static readonly Error UnsupportedAssetClass =
        new("Validation.UnsupportedAssetClass", "Only Stock and Etf instruments can be on the watchlist.");

    public static Error NotFound(Guid id) =>
        new("NotFound.WatchlistItem", $"Watchlist item '{id}' was not found.");

    public static Error AlreadyWatched(Guid instrumentId) =>
        new("Conflict.InstrumentAlreadyWatched", $"Instrument '{instrumentId}' is already on the watchlist.");
}
