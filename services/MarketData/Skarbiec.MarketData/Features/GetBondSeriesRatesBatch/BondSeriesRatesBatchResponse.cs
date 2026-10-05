namespace Skarbiec.MarketData.Features.GetBondSeriesRatesBatch;

/// <summary>PeriodIndex is zero-based; RatePercent is the annual rate in percent for that period.</summary>
public sealed record BondSeriesPeriodRateResult(int PeriodIndex, decimal RatePercent);

/// <summary>A code absent from the catalog is absent from the response.</summary>
public sealed record BondSeriesRatesResult(string Code, IReadOnlyList<BondSeriesPeriodRateResult> PeriodRates);

public sealed record BondSeriesRatesBatchResponse(IReadOnlyList<BondSeriesRatesResult> Series);
