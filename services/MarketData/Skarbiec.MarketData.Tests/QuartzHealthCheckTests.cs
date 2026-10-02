using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class QuartzHealthCheckTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private readonly SkarbiecContainersFixture _containers = containers;

    [Fact]
    public async Task SchedulerRunning_QuartzCheckIsHealthy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await FreshQuartzHost.CreateMigratedDatabaseAsync(_containers, cancellationToken);

        // Not disposed on purpose, see PriceSyncSchedulingTests.
        var host = FreshQuartzHost.BuildSchedulerHost(_containers, connectionString);
        await host.StartAsync(cancellationToken);
        try
        {
            await FreshQuartzHost.WaitForRunningSchedulerAsync(host, cancellationToken);

            var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(cancellationToken);

            var quartz = Assert.Single(report.Entries, e => e.Key.Contains("quartz", StringComparison.OrdinalIgnoreCase));
            Assert.True(quartz.Value.Status == HealthStatus.Healthy, $"{quartz.Key}: {quartz.Value.Status} {quartz.Value.Description} {quartz.Value.Exception}");
        }
        finally
        {
            await host.StopAsync(cancellationToken);
        }
    }
}
