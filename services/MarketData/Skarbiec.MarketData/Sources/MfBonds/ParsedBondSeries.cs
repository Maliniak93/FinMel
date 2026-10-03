using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Sources.MfBonds;

/// <summary>Prices in PLN per bond; MarginPercent and PeriodRates in percent, PeriodRates in period order.</summary>
public sealed record ParsedBondSeries(
    string Code,
    TreasuryBondType Type,
    string Isin,
    DateOnly SaleStart,
    DateOnly SaleEnd,
    decimal IssuePrice,
    decimal? SwapPrice,
    decimal? MarginPercent,
    IReadOnlyList<decimal> PeriodRates);
