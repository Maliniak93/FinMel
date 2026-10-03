using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Features.ListBondSeries;

/// <summary>Prices in PLN per bond; MarginPercent and FirstPeriodRatePercent in percent, the latter null until MF publishes it.</summary>
public sealed record BondSeriesListItem(
    string Code,
    TreasuryBondType Type,
    string Isin,
    DateOnly SaleStart,
    DateOnly SaleEnd,
    decimal IssuePrice,
    decimal? SwapPrice,
    decimal? MarginPercent,
    decimal? FirstPeriodRatePercent);
