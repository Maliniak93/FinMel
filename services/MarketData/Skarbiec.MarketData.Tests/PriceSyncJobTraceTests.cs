using System.Collections.Concurrent;
using System.Diagnostics;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// T2.10 AC: "Trace: job span -&gt; publish span linked" — proves the <c>DailyPricesSynced</c> publish
/// activity MassTransit emits (Diagnostics source "MassTransit", ADR-012) lands in the same distributed
/// trace as <see cref="PriceSyncJob"/>'s own "PriceSyncJob.Run" span, the same way
/// <see cref="PriceSyncSchedulingTests"/> proves the job span itself exists (T2.6 AC). Trace context
/// propagation is automatic (System.Diagnostics.Activity ambient parenting, dotnet.md) — nothing in
/// <see cref="PriceSyncJob"/> sets it manually.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class PriceSyncJobTraceTests(SkarbiecContainersFixture containers)
{
    [Fact]
    public async Task RunAsync_SuccessfulRun_PublishActivitySharesTraceWithJobActivity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var activities = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is PriceSyncJob.ActivitySourceName or "MassTransit",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activities.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await using var provider = HostlessOutboxProvider.Build<MarketDataDbContext>(containers, _ => { });
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MarketDataDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await db.Database.MigrateAsync(cancellationToken);
        await containers.ResetDatabaseAsync();

        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL.US",
            Name = "Apple",
            Source = PriceSource.Stooq,
            QuoteCurrency = "PLN", // no FX hop needed (ADR-008 base currency) — keeps this test focused.
            AssetClass = AssetClass.Stock,
        };
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        var source = new ScriptedPriceSource(PriceSource.Stooq, PriceFetchResult<InstrumentQuote>.Success(
            [new InstrumentQuote(instrument.Id, new DateOnly(2026, 8, 6), 100m)]));
        var noFx = new ScriptedFxRateSource();

        var job = new PriceSyncJob(db, [source], noFx, publishEndpoint, TimeProvider.System, NullLogger<PriceSyncJob>.Instance);
        await job.RunAsync(cancellationToken);

        var jobActivity = Assert.Single(activities, a => a.OperationName == "PriceSyncJob.Run");
        var publishActivity = Assert.Single(activities, a => a.Source.Name == "MassTransit");

        Assert.Equal(jobActivity.TraceId, publishActivity.TraceId);
    }
}
