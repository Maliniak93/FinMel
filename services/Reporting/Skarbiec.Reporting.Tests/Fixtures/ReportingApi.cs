using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.GetDashboard;
using Skarbiec.Reporting.Features.TargetAllocation.GetTargetAllocation;

namespace Skarbiec.Reporting.Tests.Fixtures;

// Arrange only, so a test of an endpoint calls it directly; snapshots have no HTTP write path, so seeds go through the DbContext.
internal static class ReportingApi
{
    public const string DashboardUri = "/api/reporting/dashboard";
    public const string NetWorthHistoryBaseUri = "/api/reporting/net-worth-history";
    public const string TargetAllocationUri = "/api/reporting/target-allocation";

    public static object TargetGroup(string name, AssetClass[] assetClasses, decimal targetPercent, decimal bandPp = 5m) =>
        new { name, assetClasses, targetPercent, bandPp };

    public static async Task SaveTargetAllocationAsync(
        this HttpClient client, CancellationToken cancellationToken, params object[] groups)
    {
        using var response = await client.PutAsJsonAsync(TargetAllocationUri, new { groups }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static async Task<TargetAllocationResponse> GetTargetAllocationAsync(
        this HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(TargetAllocationUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TargetAllocationResponse>(cancellationToken))!;
    }

    public static async Task SeedValuationAsync(
        this ReportingDbContext db,
        Guid userId,
        DateOnly date,
        (AssetClass AssetClass, decimal ValuePln)[] lines,
        CancellationToken cancellationToken,
        bool isStale = false)
    {
        await db.SeedSnapshotAsync(userId, date, lines.Sum(l => l.ValuePln), cancellationToken, isStale);
        foreach (var (assetClass, valuePln) in lines)
        {
            await db.SeedValuationLineAsync(
                userId, null, Guid.NewGuid(), date, valuePln, cancellationToken, assetClass: assetClass, isStale: isStale);
        }
    }

    public static async Task AssertProblemAsync(
        this HttpResponseMessage response, HttpStatusCode status, string errorCode, CancellationToken cancellationToken)
    {
        Assert.Equal(status, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.True(problem.Extensions.TryGetValue("errorCode", out var rawCode), "The ProblemDetails carries no errorCode extension.");
        Assert.Equal(errorCode, rawCode is JsonElement element ? element.GetString() : rawCode?.ToString());
    }

    public static string NetWorthHistoryUri(string? range = null, AssetClass? assetClass = null)
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

        return parameters.Count > 0 ? $"{NetWorthHistoryBaseUri}?{string.Join('&', parameters)}" : NetWorthHistoryBaseUri;
    }

    public static async Task SeedSnapshotAsync(
        this ReportingDbContext db,
        Guid userId,
        DateOnly date,
        decimal totalPln,
        CancellationToken cancellationToken,
        bool isStale = false)
    {
        db.ValuationSnapshots.Add(new ValuationSnapshot
        {
            Id = Guid.NewGuid(),
            UserId = userId,
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
        Guid? accountId,
        CancellationToken cancellationToken,
        AssetClass assetClass = AssetClass.Stock,
        AssetValuationMode valuationMode = AssetValuationMode.CurrencyValued,
        Guid? instrumentId = null,
        string currency = "PLN",
        decimal quantity = 1m,
        long version = 0,
        bool isArchived = false,
        IReadOnlyList<(DateOnly Date, decimal Quantity)>? quantityHistory = null,
        DateOnly? archivedOn = null)
    {
        db.Positions.Add(new Data.Position
        {
            AssetId = assetId,
            UserId = userId,
            AccountId = accountId,
            AssetClass = assetClass,
            ValuationMode = valuationMode,
            InstrumentId = instrumentId,
            Currency = currency,
            Quantity = quantity,
            IsArchived = isArchived,
            Version = version,
            UpdatedAt = DateTimeOffset.UtcNow,
            QuantityHistory = [.. (quantityHistory ?? []).Select(p => new PositionQuantityPoint { Date = p.Date, Quantity = p.Quantity })],
            ArchivedOn = archivedOn,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedHistoryRebuildRequestAsync(
        this ReportingDbContext db,
        Guid userId,
        DateOnly fromDate,
        CancellationToken cancellationToken,
        long revision = 1)
    {
        db.HistoryRebuildRequests.Add(new HistoryRebuildRequest
        {
            UserId = userId,
            FromDate = fromDate,
            Revision = revision,
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    public static async Task SeedValuationLineAsync(
        this ReportingDbContext db,
        Guid userId,
        Guid? accountId,
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
            AccountId = accountId,
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
