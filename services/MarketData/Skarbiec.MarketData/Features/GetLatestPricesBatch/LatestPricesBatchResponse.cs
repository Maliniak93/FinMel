namespace Skarbiec.MarketData.Features.GetLatestPricesBatch;

/// <summary>Latest quote on or before AsOfDate; an instrument absent from the response has none.</summary>
public sealed record InstrumentQuoteResult
{
    public required Guid InstrumentId { get; init; }
    public required string QuoteCurrency { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Close { get; init; }
}

public sealed record LatestPricesBatchResponse
{
    public required IReadOnlyList<InstrumentQuoteResult> Quotes { get; init; }
}
