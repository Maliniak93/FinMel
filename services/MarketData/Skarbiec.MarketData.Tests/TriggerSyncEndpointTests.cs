using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

// This host has no scheduler, so SyncTriggerSchedulingTests cover the firing and the double-click guard.
public sealed class TriggerSyncEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    // An explicit field: the primary constructor parameter also goes to the base constructor, so using it here would trigger CS9107.
    private readonly SkarbiecContainersFixture _containers = containers;

    private const string TriggerUri = "/api/marketdata/sync/trigger";

    [Fact]
    public async Task Post_WithToken_ReturnsNoContent()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsync(TriggerUri, null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task WhileStartupSyncRuns_ReturnsAlreadyRunning()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using (var seedDb = CreateDbContext())
        {
            await seedDb.SeedSyncRunAsync(SyncRunKind.Prices, SyncRunStatus.Running, DateTimeOffset.UtcNow, cancellationToken, SyncRunSource.Startup);
        }

        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.PostAsync(TriggerUri, null, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken);
        Assert.Equal("Conflict.SyncAlreadyRunning", problem!.Extensions["errorCode"]?.ToString());
    }

    [Fact]
    public async Task ManualRun_RecordsManualSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = await SyncHost.StartAsync(
            _containers,
            new NoOpFxRateSource(),
            FakeMfBondSource.FromFixture(),
            cancellationToken,
            new Dictionary<string, string?> { ["StartupSync:Enabled"] = "false" });
        try
        {
            var outcome = await host.Services.GetRequiredService<ISyncTrigger>().TriggerAsync(cancellationToken);
            Assert.Equal(SyncTriggerOutcome.Started, outcome);

            await using var db = CreateDbContext();
            await SyncHost.WaitUntilAsync(
                () => db.SyncRuns.AsNoTracking().AnyAsync(r => r.FinishedAt != null, cancellationToken),
                cancellationToken);

            var run = await db.SyncRuns.AsNoTracking().SingleAsync(cancellationToken);
            Assert.Equal(SyncRunKind.Prices, run.Kind);
            Assert.Equal(SyncRunSource.Manual, run.Source);
        }
        finally
        {
            await SyncHost.CleanUpAsync(host, cancellationToken);
        }
    }

    [Fact]
    public async Task Post_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsync(TriggerUri, null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
