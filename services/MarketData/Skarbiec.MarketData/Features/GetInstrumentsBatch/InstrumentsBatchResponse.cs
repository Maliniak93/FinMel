namespace Skarbiec.MarketData.Features.GetInstrumentsBatch;

/// <summary>LastPrice is the latest close on or before today; both last-price fields are null when the instrument has none.</summary>
public sealed record InstrumentBatchItem
{
    public required Guid InstrumentId { get; init; }
    public required string Ticker { get; init; }
    public required string Name { get; init; }
    public required string? Exchange { get; init; }
    public required string QuoteCurrency { get; init; }
    public required decimal? LastPrice { get; init; }
    public required DateOnly? LastPriceDate { get; init; }

    /// <summary>The latest close dated strictly before LastPriceDate; both previous-close fields are null when there is none.</summary>
    public required decimal? PreviousClose { get; init; }

    public required DateOnly? PreviousCloseDate { get; init; }
}

public sealed record InstrumentsBatchResponse
{
    public required IReadOnlyList<InstrumentBatchItem> Instruments { get; init; }
}
