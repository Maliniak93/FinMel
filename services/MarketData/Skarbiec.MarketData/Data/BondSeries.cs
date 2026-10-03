using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Data;

// Global reference data from the MF retail bond file; Code is the series code such as "EDO1036".
public sealed class BondSeries
{
    public required string Code { get; init; }
    public required TreasuryBondType Type { get; set; }
    public required string Isin { get; set; }
    public required DateOnly SaleStart { get; set; }
    public required DateOnly SaleEnd { get; set; }
    public required decimal IssuePrice { get; set; }
    public decimal? SwapPrice { get; set; }
    public decimal? MarginPercent { get; set; }
    public required DateTimeOffset UpdatedAtUtc { get; set; }
    public List<BondSeriesPeriodRate> PeriodRates { get; init; } = [];
}
