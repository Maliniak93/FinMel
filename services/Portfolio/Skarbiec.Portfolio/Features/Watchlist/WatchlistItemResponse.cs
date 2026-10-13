using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Securities;

namespace Skarbiec.Portfolio.Features.Watchlist;

public sealed record WatchlistItemResponse
{
    public required Guid Id { get; init; }
    public required Guid InstrumentId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required string Ticker { get; init; }
    public required string Name { get; init; }
    public required string? Exchange { get; init; }
    public required string QuoteCurrency { get; init; }
    public required string? Note { get; init; }

    /// <summary>In the instrument's quote currency.</summary>
    public required decimal? TargetPrice { get; init; }

    public required decimal? LastPrice { get; init; }
    public required DateOnly? LastPriceDate { get; init; }
    public required decimal? PreviousClose { get; init; }

    /// <summary>Last price minus previous close, in the quote currency.</summary>
    public required decimal? DailyChange { get; init; }

    public required decimal? DailyChangePercent { get; init; }
    public required decimal? LastPricePln { get; init; }

    /// <summary>Percent of the target the last price sits above; negative below it.</summary>
    public required decimal? TargetDistancePercent { get; init; }

    public required bool AtOrBelowTarget { get; init; }
    public required SecurityPriceUnavailableReason? PriceUnavailableReason { get; init; }

    /// <summary>The user's unarchived assets of this instrument in unarchived accounts, quantity 0 included.</summary>
    public required IReadOnlyList<WatchlistHoldingResponse> Holdings { get; init; }

    public required decimal HeldQuantity { get; init; }
}

public sealed record WatchlistHoldingResponse
{
    public required Guid AccountId { get; init; }
    public required string AccountName { get; init; }
    public required Guid AssetId { get; init; }
    public required string AssetName { get; init; }
    public required decimal Quantity { get; init; }
}
