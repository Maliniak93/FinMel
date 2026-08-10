namespace Skarbiec.MarketData.Features.GetLatestPricesBatch;

/// <summary>
/// Latest quote at or before <c>AsOfDate</c> for one instrument (the "no quote today → last
/// known" rule, 03-domain-model.md §valuation — Reporting compares <see cref="Date"/> against the
/// snapshot date itself to decide staleness). An instrument absent from the response has no quote
/// at all on or before <c>AsOfDate</c>.
/// </summary>
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
