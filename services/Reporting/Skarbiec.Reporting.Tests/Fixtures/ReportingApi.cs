using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.GetDashboard;

namespace Skarbiec.Reporting.Tests.Fixtures;

// Arrange only, so a test of an endpoint calls it directly; snapshots have no HTTP write path, so seeds go through the DbContext.
internal static class ReportingApi
{
    public const string DashboardUri = "/api/reporting/dashboard";
    public const string NetWorthHistoryBaseUri = "/api/reporting/net-worth-history";

    public static string DashboardUri_ForPortfolio(Guid portfolioId) =>
        $"{DashboardUri}?portfolioId={portfolioId}";

    public static string NetWorthHistoryUri(string? range = null, Guid? portfolioId = null, AssetClass? assetClass = null)
    {
        var parameters = new List<string>();
        if (assetClass is not null)
        {
            parameters.Add($"assetClass={assetClass}");
        }

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
        long version = 0,
        bool isArchived = false,
        IReadOnlyList<(DateOnly Date, decimal Quantity)>? quantityHistory = null,
        DateOnly? archivedOn = null,
        DateOnly? portfolioArchivedOn = null)
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
            IsArchived = isArchived,
            Version = version,
            UpdatedAt = DateTimeOffset.UtcNow,
            QuantityHistory = [.. (quantityHistory ?? []).Select(p => new PositionQuantityPoint { Date = p.Date, Quantity = p.Quantity })],
            ArchivedOn = archivedOn,
            PortfolioArchivedOn = portfolioArchivedOn,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedHistoryRebuildRequestAsync(
        this ReportingDbContext db,
        Guid userId,
        Guid portfolioId,
        DateOnly fromDate,
        CancellationToken cancellationToken,
        long revision = 1)
    {
        db.HistoryRebuildRequests.Add(new HistoryRebuildRequest
        {
            PortfolioId = portfolioId,
            UserId = userId,
            FromDate = fromDate,
            Revision = revision,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

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

    // A message published before the consumer's queue is bound would be dropped.
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

    // After 15 s returns the last response as-is, so the caller's assertion reports what the dashboard said.
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

            // Read as a string: ReadFromJsonAsync disposes the cached stream, and the caller reads this response again.
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
