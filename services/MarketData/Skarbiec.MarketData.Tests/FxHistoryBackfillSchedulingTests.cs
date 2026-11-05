using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

// Builds its own provider, with no HTTP host, through the shared consumer harness.
public sealed class FxHistoryBackfillSchedulingTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    public ValueTask InitializeAsync() => new(ConsumerTestHost.MigrateAndResetAsync(containers));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task EarlierFirstTransaction_EnqueuesJob()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedCoveredCatalogAsync(cancellationToken);

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var fxTrigger = provider.GetRequiredService<FakeFxHistoryBackfillTrigger>();

            await provider.GetRequiredService<IBus>().Publish(
                PositionEvents.CashChanged(Guid.NewGuid(), version: 0, firstTransactionDate: Today.AddDays(-1000)), cancellationToken);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline && fxTrigger.Attempts == 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }

            Assert.True(fxTrigger.Attempts >= 1, "The FX backfill was not enqueued within the deadline.");
        }, cancellationToken);
    }

    [Fact]
    public async Task CoveredDate_DoesNotEnqueue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await SeedCoveredCatalogAsync(cancellationToken);
        var assetId = Guid.NewGuid();

        await ConsumerTestHost.RunAsync(containers, async (provider, _) =>
        {
            var fxTrigger = provider.GetRequiredService<FakeFxHistoryBackfillTrigger>();

            await provider.GetRequiredService<IBus>().Publish(
                PositionEvents.CashChanged(assetId, version: 0, firstTransactionDate: Today.AddDays(-10)), cancellationToken);

            // The link is saved before the enqueue decision, so its appearance (plus a grace period) means the consumer has decided.
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
                if (await db.AssetInstrumentLinks.AnyAsync(l => l.AssetId == assetId, cancellationToken))
                {
                    break;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            Assert.Equal(0, fxTrigger.Attempts);
        }, cancellationToken);
    }

    // Coverage sits exactly at the minimum horizon, so only a transaction older than that makes a currency lag.
    private async Task SeedCoveredCatalogAsync(CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<MarketDataDbContext>().UseNpgsql(containers.PostgresConnectionString).Options;
        await using var db = new MarketDataDbContext(options);
        await db.SeedCurrencyCatalogAsync(cancellationToken, historyCoveredFrom: Today.AddDays(-365 - 14));
    }
}
