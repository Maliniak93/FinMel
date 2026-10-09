using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Messaging;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Messaging;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using static Skarbiec.Reporting.Tests.Fixtures.ReportingConsumers;

namespace Skarbiec.Reporting.Tests;

// Timed from publishing the request to its deletion, which happens in the same save as the last rewritten row.
[Collection(TestingDefaults.SerialCollectionName)]
public sealed class PortfolioHistoryRebuildPerformanceTests(SkarbiecContainersFixture containers) : IAsyncLifetime, IClassFixture<SkarbiecContainersFixture>
{
    private const string QueueName = "portfolio-history-rebuild-performance-test";
    private const int Days = 3 * 365;

    public async ValueTask InitializeAsync() => await MigrateAndResetAsync(containers);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task ThreeYearsTwentyPositions_CompletesUnderTenSeconds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var today = Today;
        var from = today.AddDays(-Days);
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var client = new FakePriceQuoteClient();

        await using (var db = OpenDbContext(containers))
        {
            for (var i = 0; i < 10; i++)
            {
                var instrumentId = Guid.NewGuid();
                await db.SeedPositionAsync(Guid.NewGuid(), userId, portfolioId, cancellationToken,
                    assetClass: AssetClass.Stock, valuationMode: AssetValuationMode.Market, instrumentId: instrumentId,
                    currency: "USD", quantity: 30m,
                    quantityHistory: [(from, 10m), (from.AddDays(400), 20m), (from.AddDays(800), 30m)]);
                client.WithPriceHistory(instrumentId, "USD", Series(from, today, i => 100m + i % 50));
            }

            for (var i = 0; i < 10; i++)
            {
                await db.SeedPositionAsync(Guid.NewGuid(), userId, portfolioId, cancellationToken,
                    assetClass: AssetClass.Cash, valuationMode: AssetValuationMode.CurrencyValued,
                    currency: i % 2 == 0 ? "EUR" : "USD", quantity: 5_000m,
                    quantityHistory: [(from.AddDays(i), 1_000m), (from.AddDays(500), 5_000m)]);
            }

            await db.SeedHistoryRebuildRequestAsync(userId, portfolioId, from, cancellationToken);
        }

        client.WithFxHistory("USDPLN", Series(from, today, i => 3.8m + i % 20 / 100m));
        client.WithFxHistory("EURPLN", Series(from, today, i => 4.2m + i % 20 / 100m));

        await RunAsync(
            containers,
            x => x.AddConsumer<PortfolioHistoryRebuildConsumer>(typeof(TestConsumerDefinition)),
            async provider =>
            {
                var stopwatch = Stopwatch.StartNew();
                await provider.GetRequiredService<IBus>().PublishRebuildRequestedAsync(portfolioId, userId, cancellationToken);
                await WaitForNoRebuildRequestAsync(provider, portfolioId, cancellationToken);
                stopwatch.Stop();

                var snapshots = await GetAllSnapshotsAsync(containers, portfolioId, cancellationToken);
                Assert.Equal(Days, snapshots.Count);
                Assert.True(
                    stopwatch.ElapsedMilliseconds < 10_000,
                    $"The rebuild took {stopwatch.ElapsedMilliseconds} ms for 20 positions over {Days} days, expected < 10000 ms.");
            },
            cancellationToken,
            client);
    }

    private static (DateOnly Date, decimal Value)[] Series(DateOnly from, DateOnly to, Func<int, decimal> value) =>
        [.. Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(i => (from.AddDays(i), value(i)))];

    private sealed class TestConsumerDefinition : IdempotentConsumerDefinition<PortfolioHistoryRebuildConsumer, ReportingDbContext>
    {
        public TestConsumerDefinition() => Endpoint(e => e.Name = QueueName);
    }
}
