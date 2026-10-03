using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Features.GetBondSeries;

/// <summary>Prices in PLN per bond; MarginPercent in percent; PeriodRates holds every rate MF has published so far.</summary>
public sealed record BondSeriesResponse(
    string Code,
    TreasuryBondType Type,
    string Isin,
    DateOnly SaleStart,
    DateOnly SaleEnd,
    decimal IssuePrice,
    decimal? SwapPrice,
    decimal? MarginPercent,
    IReadOnlyList<BondSeriesPeriodRateResponse> PeriodRates);

/// <summary>PeriodIndex is zero-based; RatePercent is the annual rate in percent for that period.</summary>
public sealed record BondSeriesPeriodRateResponse(int PeriodIndex, decimal RatePercent);
