namespace Skarbiec.Portfolio.Features.Securities;

public enum SecurityPriceUnavailableReason
{
    NoQuote,

    FxRateMissing,

    MarketDataUnavailable,
}

public sealed record SecuritiesResponse
{
    public required IReadOnlyList<SecurityHoldingResponse> Holdings { get; init; }
    public required SecuritiesTotalsResponse Totals { get; init; }
}

public sealed record SecurityHoldingResponse
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required string PortfolioName { get; init; }
    public required string Name { get; init; }
    public required Guid InstrumentId { get; init; }
    public required string? Ticker { get; init; }
    public required string? Exchange { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }

    /// <summary>Weighted-average buy price in the quote currency; null at quantity 0.</summary>
    public required decimal? AverageBuyPrice { get; init; }

    /// <summary>Cost of the open position in PLN at each buy's frozen rate; null when any buy had no rate.</summary>
    public required decimal? CostPln { get; init; }

    public required decimal? LastPrice { get; init; }
    public required DateOnly? LastPriceDate { get; init; }
    public required decimal? ValuePln { get; init; }

    /// <summary>Gain in the quote currency.</summary>
    public required decimal? UnrealizedPl { get; init; }

    public required decimal? UnrealizedPlPercent { get; init; }
    public required decimal? UnrealizedPlPln { get; init; }
    public required SecurityPriceUnavailableReason? PriceUnavailableReason { get; init; }
}

/// <summary>Each total sums the holdings where that field is not null.</summary>
public sealed record SecuritiesTotalsResponse
{
    public required decimal ValuePln { get; init; }
    public required decimal CostPln { get; init; }
    public required decimal UnrealizedPlPln { get; init; }
}
