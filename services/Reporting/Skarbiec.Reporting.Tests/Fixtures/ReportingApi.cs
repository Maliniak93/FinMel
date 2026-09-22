using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;

namespace Skarbiec.Reporting.Tests.Fixtures;

/// <summary>
/// Reporting's HTTP surface as arrange-step helpers: route builders, plus the "seed me a snapshot"
/// call slice tests need before exercising the endpoint they actually care about.
/// </summary>
/// <remarks>
/// Arrange only. A test asserting on one of these endpoints must call it directly and assert on the
/// raw <see cref="HttpResponseMessage"/>. There is no HTTP write path for <see cref="ValuationSnapshot"/>
/// — only the <c>DailyPricesSynced</c> consumer (T2.11) writes it — so seeding goes straight through
/// the DbContext instead of a POST, mirroring MarketData's <c>SeedQuoteAsync</c>/<c>SeedFxRateAsync</c>.
/// </remarks>
internal static class ReportingApi
{
    public const string DashboardUri = "/api/reporting/dashboard";
    public const string NetWorthHistoryBaseUri = "/api/reporting/net-worth-history";

    public static string DashboardUri_ForPortfolio(Guid portfolioId) =>
        $"{DashboardUri}?portfolioId={portfolioId}";

    public static string NetWorthHistoryUri(string? range = null, Guid? portfolioId = null)
    {
        var parameters = new List<string>();
        if (range is not null)
        {
            parameters.Add($"range={Uri.EscapeDataString(range)}");
        }

        if (portfolioId is not null)
        {
            parameters.Add($"portfolioId={portfolioId}");
        }

        return parameters.Count > 0 ? $"{NetWorthHistoryBaseUri}?{string.Join('&', parameters)}" : NetWorthHistoryBaseUri;
    }

    /// <summary>Seeds one <see cref="ValuationSnapshot"/> row. spec-03 drops the JSONB breakdown — the
    /// dashboard's per-asset-class breakdown now comes from <see cref="Data.AssetValuation"/> rows
    /// (<see cref="SeedValuationLineAsync"/>) sharing the same (<paramref name="portfolioId"/>, <paramref name="date"/>).</summary>
    public static async Task SeedSnapshotAsync(
        this ReportingDbContext db,
        Guid userId,
        Guid portfolioId,
        DateOnly date,
        decimal totalPln,
        CancellationToken cancellationToken,
        bool isStale = false)
    {
        db.ValuationSnapshots.Add(new ValuationSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PortfolioId = portfolioId,
            Date = date,
            TotalPln = totalPln,
            IsStale = isStale,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Seeds one <see cref="Data.Position"/> row (spec-03) — the read model an
    /// <c>AssetPositionChanged</c> consumer would otherwise upsert. Used by consumer/valuation tests
    /// that need a position on the books before publishing an event against it.</summary>
    public static async Task SeedPositionAsync(
        this ReportingDbContext db,
        Guid assetId,
        Guid userId,
        Guid portfolioId,
        CancellationToken cancellationToken,
        AssetClass assetClass = AssetClass.Stock,
        AssetValuationMode valuationMode = AssetValuationMode.Manual,
        Guid? instrumentId = null,
        string currency = "PLN",
        decimal quantity = 1m,
        decimal? manualValueAmount = null,
        DateOnly? manualValueDate = null,
        bool portfolioIsArchived = false,
        long version = 0)
    {
        db.Positions.Add(new Data.Position
        {
            AssetId = assetId,
            UserId = userId,
            PortfolioId = portfolioId,
            AssetClass = assetClass,
            ValuationMode = valuationMode,
            InstrumentId = instrumentId,
            Currency = currency,
            Quantity = quantity,
            ManualValueAmount = manualValueAmount,
            ManualValueDate = manualValueDate,
            PortfolioIsArchived = portfolioIsArchived,
            Version = version,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Seeds one <see cref="Data.AssetValuation"/> line (spec-03) — the per-asset history row
    /// the <c>DailyPricesSynced</c> consumer writes; the dashboard's <c>ByAssetClass</c> breakdown is
    /// a <c>GROUP BY AssetClass</c> over these for the latest (PortfolioId, Date) per portfolio.</summary>
    public static async Task SeedValuationLineAsync(
        this ReportingDbContext db,
        Guid userId,
        Guid portfolioId,
        Guid assetId,
        DateOnly date,
        decimal valuePln,
        CancellationToken cancellationToken,
        AssetClass assetClass = AssetClass.Cash,
        decimal quantity = 1m,
        decimal? priceUsed = null,
        DateOnly? priceDate = null,
        decimal? fxRateUsed = null,
        bool isStale = false)
    {
        db.AssetValuations.Add(new Data.AssetValuation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PortfolioId = portfolioId,
            AssetId = assetId,
            Date = date,
            AssetClass = assetClass,
            Quantity = quantity,
            PriceUsed = priceUsed,
            PriceDate = priceDate,
            FxRateUsed = fxRateUsed,
            ValuePln = valuePln,
            IsStale = isStale,
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
