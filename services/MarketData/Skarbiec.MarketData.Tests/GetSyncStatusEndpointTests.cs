using System.Net;
using System.Net.Http.Json;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.GetSyncStatus;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>HTTP-level behavior of Features/GetSyncStatus (T2.14) — the "last sync" status the manual
/// trigger button reads.</summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class GetSyncStatusEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private const string StatusUri = "/api/marketdata/sync/status";

    [Fact]
    public async Task Get_NoRunYet_ReturnsHasRunFalse()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(StatusUri, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadFromJsonAsync<SyncStatusResponse>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.HasRun);
        Assert.Null(body.RunId);
    }

    [Fact]
    public async Task Get_AfterARun_ReturnsTheMostRecentOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var seedDb = CreateDbContext();
        var olderRun = new SyncRun
        {
            Id = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow.AddDays(-1),
            FinishedAt = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(1),
            Status = SyncRunStatus.Completed,
            SyncedCount = 5,
        };
        var latestRun = new SyncRun
        {
            Id = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow.AddSeconds(30),
            Status = SyncRunStatus.Partial,
            SyncedCount = 3,
            FailedCount = 1,
            NoDataCount = 2,
        };
        seedDb.SyncRuns.AddRange(olderRun, latestRun);
        await seedDb.SaveChangesAsync(cancellationToken);

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync(StatusUri, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<SyncStatusResponse>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.True(body.HasRun);
        Assert.Equal(latestRun.Id, body.RunId);
        Assert.Equal(SyncRunStatus.Partial, body.Status);
        Assert.Equal(3, body.SyncedCount);
        Assert.Equal(1, body.FailedCount);
        Assert.Equal(2, body.NoDataCount);
    }

    [Fact]
    public async Task Get_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(StatusUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
