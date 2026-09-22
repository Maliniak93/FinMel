using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.ArchivePortfolio;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.DeletePortfolio;
using Skarbiec.Portfolio.Features.DeleteTransaction;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.RestorePortfolio;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Messaging;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// Proves every position-mutating slice writes its <c>Skarbiec.Contracts.Events</c> row atomically
/// with the business row it accompanies (spec-02, ADR-012/ADR-021). Deliberately builds its own
/// <see cref="ServiceProvider"/> instead of using <see cref="PortfolioApiFactory"/>: no
/// <see cref="IHostedService"/> (MassTransit's bus, the outbox delivery poller) is ever started, so
/// the row can never be delivered/removed before the assertion runs.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class PortfolioOutboxTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private static readonly Guid UserId = Guid.NewGuid();

    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        _provider = HostlessOutboxProvider.Build<PortfolioDbContext>(containers, services =>
        {
            services.AddSingleton<ICurrentUser>(new StubCurrentUser(UserId));
            services.AddSingleton<IInstrumentLookupClient>(new FakeInstrumentLookupClient());
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<PositionEventPublisher>();
            services.AddScoped<CreatePortfolioHandler>();
            services.AddScoped<AddAssetHandler>();
            services.AddScoped<UpdateAssetHandler>();
            services.AddScoped<RecordTransactionHandler>();
            services.AddScoped<UpdateTransactionHandler>();
            services.AddScoped<DeleteTransactionHandler>();
            services.AddScoped<RemoveAssetHandler>();
            services.AddScoped<ArchivePortfolioHandler>();
            services.AddScoped<RestorePortfolioHandler>();
            services.AddScoped<DeletePortfolioHandler>();
        });

        await using (var scope = _provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>().Database.MigrateAsync();
        }

        await containers.ResetDatabaseAsync();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>spec-02 AC-1.</summary>
    [Fact]
    public async Task AddAsset_WritesAssetPositionChangedInSameTransactionAsAssetRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        Assert.True(portfolioResult.IsSuccess);

        var handler = scope.ServiceProvider.GetRequiredService<AddAssetHandler>();
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Outbox test asset",
            Currency = "PLN",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var result = await handler.HandleAsync(portfolioResult.Value.Id, request, cancellationToken);
        Assert.True(result.IsSuccess);

        var asset = await dbContext.Assets.SingleOrDefaultAsync(a => a.Id == result.Value.Id, cancellationToken);
        Assert.NotNull(asset);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var evt = Assert.Single(events);
        Assert.Equal(asset.Id, evt.AssetId);
        Assert.Equal(portfolioResult.Value.Id, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.Equal(AssetClass.Cash, evt.AssetClass);
        Assert.Equal("PLN", evt.Currency);
        Assert.Equal(100m, evt.ManualValueAmount);
        Assert.False(evt.PortfolioIsArchived);
    }

    /// <summary>spec-02 AC-2: post-update ValuationMode, InstrumentId, Currency and ManualValue* travel on the event.</summary>
    [Fact]
    public async Task UpdateAsset_WritesAssetPositionChangedWithNewValuationMode()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);
        Assert.True(assetResult.IsSuccess);

        var instrumentId = Guid.NewGuid();
        var updateResult = await scope.ServiceProvider.GetRequiredService<UpdateAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            assetResult.Value.Id,
            new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Now market", Currency = "USD", InstrumentId = instrumentId },
            cancellationToken);
        Assert.True(updateResult.IsSuccess);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var updateEvent = Assert.Single(events, e => e.Version > 0);
        Assert.Equal(AssetValuationMode.Market, updateEvent.ValuationMode);
        Assert.Equal(instrumentId, updateEvent.InstrumentId);
        Assert.Equal("USD", updateEvent.Currency);
        Assert.Null(updateEvent.ManualValueAmount);
        Assert.Null(updateEvent.ManualValueDate);
    }

    /// <summary>spec-02 AC-3: published Quantity equals the recomputed quantity.</summary>
    [Fact]
    public async Task RecordTransaction_WritesAssetPositionChangedWithRecomputedQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Stock, Name = "Shares", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);

        var transactionResult = await scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            portfolioResult.Value.Id,
            assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 5m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 2) },
            cancellationToken);
        Assert.True(transactionResult.IsSuccess);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var recordEvent = Assert.Single(events, e => e.Version > 0);
        Assert.Equal(5m, recordEvent.Quantity);
    }

    /// <summary>spec-02 AC-4: editing a transaction now publishes — nothing was published before.</summary>
    [Fact]
    public async Task UpdateTransaction_WritesAssetPositionChangedWithRecomputedQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Stock, Name = "Shares", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);
        var buyResult = await scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            portfolioResult.Value.Id,
            assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 10m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 1) },
            cancellationToken);

        var updateResult = await scope.ServiceProvider.GetRequiredService<UpdateTransactionHandler>().HandleAsync(
            portfolioResult.Value.Id,
            assetResult.Value.Id,
            buyResult.Value.Id,
            new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 15m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 1) },
            cancellationToken);
        Assert.True(updateResult.IsSuccess);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var updateEvent = events.Single(e => e.Quantity == 15m);
        Assert.Equal(assetResult.Value.Id, updateEvent.AssetId);
    }

    /// <summary>spec-02 AC-5: deleting a transaction now publishes — nothing was published before.</summary>
    [Fact]
    public async Task DeleteTransaction_WritesAssetPositionChangedWithRecomputedQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Stock, Name = "Shares", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);
        var recordHandler = scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>();
        await recordHandler.HandleAsync(
            portfolioResult.Value.Id, assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 10m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 1) },
            cancellationToken);
        var sellResult = await recordHandler.HandleAsync(
            portfolioResult.Value.Id, assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Sell, Quantity = 3m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 2) },
            cancellationToken);

        var deleteResult = await scope.ServiceProvider.GetRequiredService<DeleteTransactionHandler>()
            .HandleAsync(portfolioResult.Value.Id, assetResult.Value.Id, sellResult.Value.Id, cancellationToken);
        Assert.True(deleteResult.IsSuccess);

        // The last event in write order is the delete's — matching on the quantity alone would be
        // ambiguous here, since the Buy that preceded the Sell also left the position at 10.
        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var deleteEvent = events[^1];
        Assert.Equal(10m, deleteEvent.Quantity);
        Assert.Equal(assetResult.Value.Id, deleteEvent.AssetId);
    }

    /// <summary>spec-02 AC-6.</summary>
    [Fact]
    public async Task RemoveAsset_WritesAssetRemovedInSameTransactionAsDeletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);

        var removeResult = await scope.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
            .HandleAsync(portfolioResult.Value.Id, assetResult.Value.Id, cancellationToken);
        Assert.True(removeResult.IsSuccess);

        var remainingAsset = await dbContext.Assets.SingleOrDefaultAsync(a => a.Id == assetResult.Value.Id, cancellationToken);
        Assert.Null(remainingAsset);

        var events = await dbContext.ReadPublishedAsync<AssetRemoved>(cancellationToken);
        var evt = Assert.Single(events);
        Assert.Equal(assetResult.Value.Id, evt.AssetId);
        Assert.Equal(portfolioResult.Value.Id, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
    }

    /// <summary>spec-02 AC-7.</summary>
    [Fact]
    public async Task SuccessiveMutations_PublishStrictlyIncreasingVersionPerAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);

        await scope.ServiceProvider.GetRequiredService<UpdateAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            assetResult.Value.Id,
            new UpdateAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash renamed", Currency = "PLN", ManualValue = 50m, ManualValueDate = new DateOnly(2026, 1, 2) },
            cancellationToken);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(2, events.Count);
        Assert.True(events[1].Version > events[0].Version);
    }

    /// <summary>spec-02 AC-8.</summary>
    [Fact]
    public async Task ArchivePortfolio_WritesPortfolioArchivedAndOnePositionEventPerAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var addAssetHandler = scope.ServiceProvider.GetRequiredService<AddAssetHandler>();
        await addAssetHandler.HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash 1", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);
        await addAssetHandler.HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash 2", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);

        var archiveResult = await scope.ServiceProvider.GetRequiredService<ArchivePortfolioHandler>()
            .HandleAsync(portfolioResult.Value.Id, cancellationToken);
        Assert.True(archiveResult.IsSuccess);

        var archivedEvents = await dbContext.ReadPublishedAsync<PortfolioArchived>(cancellationToken);
        Assert.Single(archivedEvents);

        var positionEvents = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken))
            .Where(e => e.PortfolioIsArchived)
            .ToList();
        Assert.Equal(2, positionEvents.Count);
        Assert.All(positionEvents, e => Assert.True(e.PortfolioIsArchived));
    }

    /// <summary>spec-02 AC-9 (outbox half — the HTTP half is <see cref="RestorePortfolioEndpointTests"/>).</summary>
    [Fact]
    public async Task RestorePortfolio_WritesPortfolioRestoredAndOnePositionEventPerAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var addAssetHandler = scope.ServiceProvider.GetRequiredService<AddAssetHandler>();
        await addAssetHandler.HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash 1", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);
        await addAssetHandler.HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash 2", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);
        await scope.ServiceProvider.GetRequiredService<ArchivePortfolioHandler>()
            .HandleAsync(portfolioResult.Value.Id, cancellationToken);

        var restoreResult = await scope.ServiceProvider.GetRequiredService<RestorePortfolioHandler>()
            .HandleAsync(portfolioResult.Value.Id, cancellationToken);
        Assert.True(restoreResult.IsSuccess);

        var restoredEvents = await dbContext.ReadPublishedAsync<PortfolioRestored>(cancellationToken);
        Assert.Single(restoredEvents);

        var positionEvents = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken))
            .Where(e => !e.PortfolioIsArchived)
            .ToList();
        // 2 from AddAsset + 2 from restore = 4 events with PortfolioIsArchived = false.
        Assert.Equal(4, positionEvents.Count);
    }

    /// <summary>
    /// spec-02 AC-10 (the "publishes nothing" half — the HTTP half is
    /// <see cref="RestorePortfolioEndpointTests.Restore_NotArchivedPortfolio_ReturnsOkAndPublishesNothing"/>).
    /// The 200 body looks identical either way, so only the outbox can tell the no-op early return
    /// from a fan-out that re-publishes every asset on a repeated click (design decision 2).
    /// </summary>
    [Fact]
    public async Task RestorePortfolio_NotArchivedPortfolio_WritesNoFurtherEvents()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);
        var positionEventsBefore = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Single(positionEventsBefore);

        // Never archived, so restore has nothing to restore.
        var restoreResult = await scope.ServiceProvider.GetRequiredService<RestorePortfolioHandler>()
            .HandleAsync(portfolioResult.Value.Id, cancellationToken);
        Assert.True(restoreResult.IsSuccess);

        Assert.Empty(await dbContext.ReadPublishedAsync<PortfolioRestored>(cancellationToken));
        var positionEventsAfter = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(positionEventsBefore.Count, positionEventsAfter.Count);
    }

    /// <summary>
    /// spec-02 design decision 2, the mirror of
    /// <see cref="RestorePortfolio_NotArchivedPortfolio_WritesNoFurtherEvents"/>: archiving an
    /// already-archived portfolio is a no-op, so neither a second <see cref="PortfolioArchived"/>
    /// nor another per-asset fan-out may be written.
    /// </summary>
    [Fact]
    public async Task ArchivePortfolio_AlreadyArchivedPortfolio_WritesNoFurtherEvents()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);

        var archiveHandler = scope.ServiceProvider.GetRequiredService<ArchivePortfolioHandler>();
        Assert.True((await archiveHandler.HandleAsync(portfolioResult.Value.Id, cancellationToken)).IsSuccess);
        var positionEventsBefore = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);

        Assert.True((await archiveHandler.HandleAsync(portfolioResult.Value.Id, cancellationToken)).IsSuccess);

        Assert.Single(await dbContext.ReadPublishedAsync<PortfolioArchived>(cancellationToken));
        var positionEventsAfter = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(positionEventsBefore.Count, positionEventsAfter.Count);
    }

    /// <summary>spec-02 AC-12.</summary>
    [Fact]
    public async Task DeletePortfolio_WritesPortfolioDeletedInSameTransactionAsDeletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);

        var deleteResult = await scope.ServiceProvider.GetRequiredService<DeletePortfolioHandler>()
            .HandleAsync(portfolioResult.Value.Id, cancellationToken);
        Assert.True(deleteResult.IsSuccess);

        var remainingPortfolio = await dbContext.Portfolios.SingleOrDefaultAsync(p => p.Id == portfolioResult.Value.Id, cancellationToken);
        Assert.Null(remainingPortfolio);

        var events = await dbContext.ReadPublishedAsync<PortfolioDeleted>(cancellationToken);
        var evt = Assert.Single(events);
        Assert.Equal(portfolioResult.Value.Id, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
    }
}
