using Quartz;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

public sealed class QuartzSchemaProvisioningTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    private readonly SkarbiecContainersFixture _containers = containers;

    [Fact]
    public async Task FreshDatabase_SchedulerCreatesQuartzSchema_AndSecondStartValidatesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await FreshQuartzHost.CreateMigratedDatabaseAsync(_containers, cancellationToken);

        // EF migrations alone must not create the Quartz schema.
        Assert.Empty(await FreshQuartzHost.QuartzTablesAsync(connectionString, cancellationToken));

        // Hosts are deliberately not disposed, see PriceSyncSchedulingTests.
        var first = FreshQuartzHost.BuildSchedulerHost(_containers, connectionString);
        await first.StartAsync(cancellationToken);
        try
        {
            var scheduler = await FreshQuartzHost.WaitForRunningSchedulerAsync(first, cancellationToken);
            Assert.Equal(SchedulerStatus.Running, scheduler.Status);

            var tables = await FreshQuartzHost.QuartzTablesAsync(connectionString, cancellationToken);
            Assert.Contains("qrtz_triggers", tables);
            Assert.Contains("qrtz_job_details", tables);
            Assert.Contains("qrtz_cron_triggers", tables);
            Assert.Contains("qrtz_locks", tables);
        }
        finally
        {
            await first.StopAsync(cancellationToken);
        }

        var second = FreshQuartzHost.BuildSchedulerHost(_containers, connectionString);
        await second.StartAsync(cancellationToken);
        try
        {
            var scheduler = await FreshQuartzHost.WaitForRunningSchedulerAsync(second, cancellationToken);
            Assert.Equal(SchedulerStatus.Running, scheduler.Status);
        }
        finally
        {
            await second.StopAsync(cancellationToken);
        }
    }
}
