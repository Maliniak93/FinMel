namespace Skarbiec.MarketData.Data;

// PeriodIndex is zero-based: 0 is MF's "w 1. okresie" / "w 1. roku".
public sealed class BondSeriesPeriodRate
{
    public required string SeriesCode { get; init; }
    public required int PeriodIndex { get; init; }
    public required decimal RatePercent { get; set; }
}
