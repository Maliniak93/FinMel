namespace Skarbiec.MarketData.Features.GetPricesHistoryBatch;

public sealed record HistoryQuote
{
    public required DateOnly Date { get; init; }
    public required decimal Close { get; init; }
}

/// <summary>Quotes ascending: the latest one before From (if any), then every quote in From..To.</summary>
public sealed record InstrumentQuoteSeries
{
    public required Guid InstrumentId { get; init; }
    public required string QuoteCurrency { get; init; }
    public required IReadOnlyList<HistoryQuote> Quotes { get; init; }
}

public sealed record PricesHistoryBatchResponse
{
    public required IReadOnlyList<InstrumentQuoteSeries> Series { get; init; }
}
