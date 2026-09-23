using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.GetDashboard;

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

    /// <summary>Seeds one <see cref="LatestFxRate"/> row (spec-07) — the last known rate the
    /// <c>DailyPricesSynced</c> consumer would otherwise have stored, which the position-event path
    /// values foreign currencies with.</summary>
    public static async Task SeedLatestFxRateAsync(
        this ReportingDbContext db,
        string pair,
        DateOnly date,
        decimal rate,
        CancellationToken cancellationToken)
    {
        db.Set<LatestFxRate>().Add(new LatestFxRate
        {
            Pair = pair,
            Date = date,
            Rate = rate,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Seeds one <see cref="LatestInstrumentPrice"/> row (spec-07) — the last known close
    /// the <c>DailyPricesSynced</c> consumer would otherwise have stored for <paramref name="instrumentId"/>.</summary>
    public static async Task SeedLatestInstrumentPriceAsync(
        this ReportingDbContext db,
        Guid instrumentId,
        string quoteCurrency,
        DateOnly date,
        decimal close,
        CancellationToken cancellationToken)
    {
        db.Set<LatestInstrumentPrice>().Add(new LatestInstrumentPrice
        {
            InstrumentId = instrumentId,
            QuoteCurrency = quoteCurrency,
            Date = date,
            Close = close,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Polls <c>/health/ready</c> until the host's bus reports started — a message published before
    /// the consumer's queue is bound would be dropped. Arrange only: throws if never ready.
    /// </summary>
    public static async Task WaitUntilReadyAsync(this HttpClient client, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }

        throw new TimeoutException("The Reporting host never reported ready.");
    }

    /// <summary>
    /// Polls <c>GET /dashboard</c> with <paramref name="client"/> until its body satisfies
    /// <paramref name="predicate"/> (an event-driven write lands asynchronously), then returns that
    /// raw response for the test to assert on. After 15 s returns the last response as-is, so the
    /// caller's own assertion reports what the dashboard actually said.
    /// </summary>
    public static async Task<HttpResponseMessage> GetDashboardWhenAsync(
        this HttpClient client, Func<DashboardResponse, bool> predicate, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (true)
        {
            var response = await client.GetAsync(DashboardUri, cancellationToken);
            if (DateTimeOffset.UtcNow >= deadline || !response.IsSuccessStatusCode)
            {
                return response;
            }

            // Read as a string, not ReadFromJsonAsync: that disposes the content's cached read
            // stream, and the caller reads this same response again.
            var body = JsonSerializer.Deserialize<DashboardResponse>(
                await response.Content.ReadAsStringAsync(cancellationToken), JsonSerializerOptions.Web);
            if (body is not null && predicate(body))
            {
                return response;
            }

            response.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }
}
