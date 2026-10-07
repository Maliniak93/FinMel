using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Features.GetMetalPrices;

/// <summary>The latest spot price of one metal per gram of fine metal; every price field is null until a quote exists, PLN ones also until a USDPLN rate does.</summary>
public sealed record MetalPriceResponse
{
    public required Metal Metal { get; init; }
    public required Guid InstrumentId { get; init; }
    public DateOnly? Date { get; init; }
    public decimal? PricePerGramUsd { get; init; }

    /// <summary>The latest USDPLN rate on or before Date.</summary>
    public decimal? UsdPlnRate { get; init; }

    /// <summary>Display only, rounded to 2 dp.</summary>
    public decimal? PricePerGramPln { get; init; }

    /// <summary>Display only, rounded to 2 dp.</summary>
    public decimal? PricePerTroyOuncePln { get; init; }

    /// <summary>True when Date is more than 7 days before today in Warsaw.</summary>
    public required bool IsStale { get; init; }
}
