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
    public required Guid? GroupId { get; init; }

    /// <summary>The asset or its account is archived; such holdings appear only with includeArchived.</summary>
    public required bool IsArchived { get; init; }

    public required Guid AccountId { get; init; }
    public required string AccountName { get; init; }
    public required string Name { get; init; }
    public required Guid InstrumentId { get; init; }
    public required string? Ticker { get; init; }
    public required string? Exchange { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }

    /// <summary>Weighted-average buy price in the quote currency; null at quantity 0.</summary>
    public required decimal? AverageBuyPrice { get; init; }

    /// <summary>Open position's cost in PLN per unit, fees included; null at quantity 0 or when any buy had no rate.</summary>
    public required decimal? AverageBuyPricePln { get; init; }

    /// <summary>Cost of the open position in PLN: each buy's PLN amount when entered, else at its frozen rate; null when any buy had neither.</summary>
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
