using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Valuation;

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

    /// <summary>Seeds one <see cref="ValuationSnapshot"/> row. Defaults to a single-entry Cash breakdown equal to <paramref name="totalPln"/> when <paramref name="breakdown"/> is omitted.</summary>
    public static async Task SeedSnapshotAsync(
        this ReportingDbContext db,
        Guid userId,
        Guid portfolioId,
        DateOnly date,
        decimal totalPln,
        CancellationToken cancellationToken,
        IReadOnlyList<AssetClassBreakdownEntry>? breakdown = null,
        bool isStale = false)
    {
        db.ValuationSnapshots.Add(new ValuationSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PortfolioId = portfolioId,
            Date = date,
            TotalPln = totalPln,
            BreakdownJson = ValuationBreakdown.Serialize(breakdown ?? [new AssetClassBreakdownEntry(AssetClass.Cash, totalPln)]),
            IsStale = isStale,
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
