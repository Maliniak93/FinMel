using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.ArchiveAsset;
using Skarbiec.Portfolio.Features.ArchivePortfolio;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.DeletePortfolio;
using Skarbiec.Portfolio.Features.DeleteTransaction;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;
using Skarbiec.Portfolio.Features.Deposits.RollOverDeposit;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.RestoreAsset;
using Skarbiec.Portfolio.Features.RestorePortfolio;
using Skarbiec.Portfolio.Features.SavingsAccounts.AddSavingsAccount;
using Skarbiec.Portfolio.Features.SavingsAccounts.SettleSavingsInterest;
using Skarbiec.Portfolio.Features.SavingsAccounts.UndoSavingsInterestSettlement;
using Skarbiec.Portfolio.Features.SavingsAccounts.UpdateSavingsAccount;
using Skarbiec.Portfolio.Features.Transfers.CreateTransfer;
using Skarbiec.Portfolio.Features.Transfers.DeleteTransfer;
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

    private readonly SaveChangesCounter _saveChanges = new();

    private ServiceProvider _provider = null!;

    public async ValueTask InitializeAsync()
    {
        _provider = HostlessOutboxProvider.Build<PortfolioDbContext>(containers, services =>
        {
            services.ConfigureDbContext<PortfolioDbContext>(options => options.AddInterceptors(_saveChanges));
            services.AddSingleton<ICurrentUser>(new StubCurrentUser(UserId));
            services.AddSingleton<IInstrumentLookupClient>(new FakeInstrumentLookupClient());
            services.AddSingleton<IFxRateLookupClient>(new FakeFxRateLookupClient());
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<PositionEventPublisher>();
            services.AddScoped<CreatePortfolioHandler>();
            services.AddScoped<AddAssetHandler>();
            services.AddScoped<UpdateAssetHandler>();
            services.AddScoped<RecordTransactionHandler>();
            services.AddScoped<UpdateTransactionHandler>();
            services.AddScoped<DeleteTransactionHandler>();
            services.AddScoped<RemoveAssetHandler>();
            services.AddScoped<AddDepositHandler>();
            services.AddScoped<AddSavingsAccountHandler>();
            services.AddScoped<UpdateSavingsAccountHandler>();
            services.AddScoped<SettleSavingsInterestHandler>();
            services.AddScoped<UndoSavingsInterestSettlementHandler>();
            services.AddScoped<UpdateDepositHandler>();
            services.AddScoped<SettleDepositHandler>();
            services.AddScoped<PayOutDepositHandler>();
            services.AddScoped<RollOverDepositHandler>();
            services.AddScoped<ArchivePortfolioHandler>();
            services.AddScoped<RestorePortfolioHandler>();
            services.AddScoped<ArchiveAssetHandler>();
            services.AddScoped<RestoreAssetHandler>();
            services.AddScoped<DeletePortfolioHandler>();
            services.AddScoped<CreateTransferHandler>();
            services.AddScoped<DeleteTransferHandler>();
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

    /// <summary>
    /// spec-08 AC-2: the portfolio, its assets and their transactions are removed and the outbox
    /// holds one <see cref="AssetRemoved"/> (cascaded) per asset plus one <see cref="PortfolioDeleted"/>
    /// — all in a single save, so no consumer can ever see a half-deleted portfolio.
    /// </summary>
    [Fact]
    public async Task DeletePortfolio_WithAssets_RemovesChildrenAndWritesAssetRemovedPerAssetAndPortfolioDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioId = (await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Cascade portfolio" }, cancellationToken)).Value.Id;
        var assetIds = new List<Guid>
        {
            await AddAssetWithBuyAsync(scope.ServiceProvider, portfolioId, "Shares 1", cancellationToken),
            await AddAssetWithBuyAsync(scope.ServiceProvider, portfolioId, "Shares 2", cancellationToken),
        };

        // A sibling portfolio the cascade must not reach.
        var siblingPortfolioId = (await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Sibling portfolio" }, cancellationToken)).Value.Id;
        var siblingAssetId = await AddAssetWithBuyAsync(scope.ServiceProvider, siblingPortfolioId, "Sibling shares", cancellationToken);

        _saveChanges.Reset();

        var deleteResult = await scope.ServiceProvider.GetRequiredService<DeletePortfolioHandler>()
            .HandleAsync(portfolioId, cancellationToken);

        Assert.True(deleteResult.IsSuccess);
        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.False(await verifyDb.Portfolios.AnyAsync(p => p.Id == portfolioId, cancellationToken));
        Assert.False(await verifyDb.Assets.AnyAsync(a => a.PortfolioId == portfolioId, cancellationToken));
        Assert.False(await verifyDb.Transactions.AnyAsync(t => assetIds.Contains(t.AssetId), cancellationToken));
        Assert.True(await verifyDb.Assets.AnyAsync(a => a.Id == siblingAssetId, cancellationToken));
        Assert.True(await verifyDb.Transactions.AnyAsync(t => t.AssetId == siblingAssetId, cancellationToken));

        var removed = await verifyDb.ReadPublishedAsync<AssetRemoved>(cancellationToken);
        Assert.Equal(assetIds.Order(), removed.Select(e => e.AssetId).Order());
        Assert.All(removed, e =>
        {
            Assert.True(e.CascadedFromPortfolio);
            Assert.Equal(portfolioId, e.PortfolioId);
            Assert.Equal(UserId, e.UserId);
        });

        var deleted = Assert.Single(await verifyDb.ReadPublishedAsync<PortfolioDeleted>(cancellationToken));
        Assert.Equal(portfolioId, deleted.PortfolioId);
        Assert.Equal(UserId, deleted.UserId);
    }

    /// <summary>
    /// spec-08 AC-4: removing an asset takes its transactions with it and writes one
    /// non-cascaded <see cref="AssetRemoved"/>, all in a single save.
    /// </summary>
    [Fact]
    public async Task RemoveAsset_WithTransactions_RemovesTransactionsAndWritesAssetRemoved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();

        var portfolioId = (await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken)).Value.Id;
        var assetId = await AddAssetWithBuyAsync(scope.ServiceProvider, portfolioId, "Shares", cancellationToken);
        var keptAssetId = await AddAssetWithBuyAsync(scope.ServiceProvider, portfolioId, "Kept shares", cancellationToken);

        _saveChanges.Reset();

        var removeResult = await scope.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken);

        Assert.True(removeResult.IsSuccess);
        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.False(await verifyDb.Assets.AnyAsync(a => a.Id == assetId, cancellationToken));
        Assert.False(await verifyDb.Transactions.AnyAsync(t => t.AssetId == assetId, cancellationToken));
        Assert.True(await verifyDb.Transactions.AnyAsync(t => t.AssetId == keptAssetId, cancellationToken));
        Assert.True(await verifyDb.Portfolios.AnyAsync(p => p.Id == portfolioId, cancellationToken));

        var evt = Assert.Single(await verifyDb.ReadPublishedAsync<AssetRemoved>(cancellationToken));
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(portfolioId, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.False(evt.CascadedFromPortfolio);
    }

    /// <summary>
    /// archived-portfolio-out-of-net-worth AC6 (outbox half): every one of the six asset/transaction
    /// writes into an archived portfolio fails with <c>Conflict.PortfolioArchived</c> before it
    /// writes anything — no business row changes and not a single outbox row is added, so Reporting
    /// never sees an event it would otherwise silently ignore. Each write runs in its own scope so a
    /// half-applied change tracked by one handler cannot hide behind another's early return.
    /// </summary>
    [Fact]
    public async Task WriteToArchivedPortfolio_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Archived outbox portfolio" }, cancellationToken)).Value.Id;
            assetId = await AddAssetWithBuyAsync(arrange.ServiceProvider, portfolioId, "Shares", cancellationToken);
            Assert.True((await arrange.ServiceProvider.GetRequiredService<ArchivePortfolioHandler>()
                .HandleAsync(portfolioId, cancellationToken)).IsSuccess);
        }

        Guid transactionId;
        int outboxRowsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            var db = before.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            transactionId = await db.Transactions.Where(t => t.AssetId == assetId).Select(t => t.Id).SingleAsync(cancellationToken);
            outboxRowsBefore = await db.Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        var writes = new (string Name, Func<IServiceProvider, Task<Error>> Write)[]
        {
            ("AddAsset", async s => (await s.GetRequiredService<AddAssetHandler>().HandleAsync(
                portfolioId,
                new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "New cash", Currency = "PLN", ManualValue = 100m, ManualValueDate = new DateOnly(2026, 1, 1) },
                cancellationToken)).Error),
            ("UpdateAsset", async s => (await s.GetRequiredService<UpdateAssetHandler>().HandleAsync(
                portfolioId,
                assetId,
                new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Renamed", Currency = "PLN", ManualValue = 1m, ManualValueDate = new DateOnly(2026, 1, 3) },
                cancellationToken)).Error),
            ("RecordTransaction", async s => (await s.GetRequiredService<RecordTransactionHandler>().HandleAsync(
                portfolioId,
                assetId,
                new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 3) },
                cancellationToken)).Error),
            ("UpdateTransaction", async s => (await s.GetRequiredService<UpdateTransactionHandler>().HandleAsync(
                portfolioId,
                assetId,
                transactionId,
                new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 50m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 2) },
                cancellationToken)).Error),
            ("DeleteTransaction", async s => (await s.GetRequiredService<DeleteTransactionHandler>()
                .HandleAsync(portfolioId, assetId, transactionId, cancellationToken)).Error),
            ("RemoveAsset", async s => (await s.GetRequiredService<RemoveAssetHandler>()
                .HandleAsync(portfolioId, assetId, cancellationToken)).Error),
        };

        foreach (var (name, write) in writes)
        {
            await using var scope = _provider.CreateAsyncScope();
            var error = await write(scope.ServiceProvider);
            Assert.True(
                error.Code == PortfolioAssertions.PortfolioArchivedErrorCode,
                $"{name} on an archived portfolio returned '{error.Code}', expected '{PortfolioAssertions.PortfolioArchivedErrorCode}'.");
        }

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        var asset = await verifyDb.Assets.SingleAsync(a => a.PortfolioId == portfolioId, cancellationToken);
        Assert.Equal(assetId, asset.Id);
        Assert.Equal("Shares", asset.Name);
        Assert.Equal(5m, asset.Quantity);
        var transaction = await verifyDb.Transactions.SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(transactionId, transaction.Id);
        Assert.Equal(5m, transaction.Quantity);
    }

    /// <summary>
    /// cash-transaction-types AC-2..AC-5 (outbox half): every write rejected with
    /// <c>Validation.TransactionTypeNotAllowed</c> — a Buy recorded on a Cash asset, a Cash
    /// asset opened with an Interest, a Cash Deposit edited into a Sell, a Stock holding a Buy turned
    /// into Cash — fails before it writes anything: not one outbox row is added and no business row
    /// moves. Each write runs in its own scope, as in <see cref="WriteToArchivedPortfolio_WritesNoEvent"/>.
    /// </summary>
    [Fact]
    public async Task DisallowedTransactionTypeOnCashLikeAsset_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid cashAssetId;
        Guid depositId;
        Guid stockAssetId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Cash rule outbox portfolio" }, cancellationToken)).Value.Id;
            var cashResult = await arrange.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
                portfolioId,
                new AddAssetRequest
                {
                    AssetClass = AssetClass.Cash,
                    Name = "Wallet",
                    Currency = "PLN",
                    InitialTransaction = new RecordTransactionRequest
                    {
                        Type = TransactionType.Deposit,
                        Quantity = 1_000m,
                        UnitPrice = 1m,
                        Date = new DateOnly(2026, 1, 1)
                    }
                },
                cancellationToken);
            Assert.True(cashResult.IsSuccess);
            cashAssetId = cashResult.Value.Id;
            stockAssetId = await AddAssetWithBuyAsync(arrange.ServiceProvider, portfolioId, "Shares", cancellationToken);
        }

        int outboxRowsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            var db = before.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            depositId = await db.Transactions.Where(t => t.AssetId == cashAssetId).Select(t => t.Id).SingleAsync(cancellationToken);
            outboxRowsBefore = await db.Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        var writes = new (string Name, Func<IServiceProvider, Task<Error>> Write)[]
        {
            ("RecordTransaction", async s => (await s.GetRequiredService<RecordTransactionHandler>().HandleAsync(
                portfolioId,
                cashAssetId,
                new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 3) },
                cancellationToken)).Error),
            ("AddAsset", async s => (await s.GetRequiredService<AddAssetHandler>().HandleAsync(
                portfolioId,
                new AddAssetRequest
                {
                    AssetClass = AssetClass.Cash,
                    Name = "Second wallet",
                    Currency = "PLN",
                    InitialTransaction = new RecordTransactionRequest
                    {
                        Type = TransactionType.Interest, Quantity = 10m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 3)
                    }
                },
                cancellationToken)).Error),
            ("UpdateTransaction", async s => (await s.GetRequiredService<UpdateTransactionHandler>().HandleAsync(
                portfolioId,
                cashAssetId,
                depositId,
                new UpdateTransactionRequest { Type = TransactionType.Sell, Quantity = 100m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) },
                cancellationToken)).Error),
            ("UpdateAsset", async s => (await s.GetRequiredService<UpdateAssetHandler>().HandleAsync(
                portfolioId,
                stockAssetId,
                new UpdateAssetRequest { AssetClass = AssetClass.Cash, Name = "Now cash", Currency = "PLN" },
                cancellationToken)).Error),
        };

        foreach (var (name, write) in writes)
        {
            await using var scope = _provider.CreateAsyncScope();
            var error = await write(scope.ServiceProvider);
            Assert.True(
                error.Code == PortfolioAssertions.TransactionTypeNotAllowedErrorCode,
                $"{name} with a type the cash-like class does not accept returned '{error.Code}', expected '{PortfolioAssertions.TransactionTypeNotAllowedErrorCode}'.");
        }

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(2, await verifyDb.Assets.CountAsync(a => a.PortfolioId == portfolioId, cancellationToken));
        var cash = await verifyDb.Assets.SingleAsync(a => a.Id == cashAssetId, cancellationToken);
        Assert.Equal(1_000m, cash.Quantity);
        var deposit = await verifyDb.Transactions.SingleAsync(t => t.AssetId == cashAssetId, cancellationToken);
        Assert.Equal(depositId, deposit.Id);
        Assert.Equal(TransactionType.Deposit, deposit.Type);
        var stock = await verifyDb.Assets.SingleAsync(a => a.Id == stockAssetId, cancellationToken);
        Assert.Equal(AssetClass.Stock, stock.AssetClass);
    }

    /// <summary>
    /// term-deposits AC-5 (outbox half): AddDeposit writes the Deposit-class asset, its opening
    /// transaction, its <see cref="TermDeposit"/> terms and one <see cref="AssetPositionChanged"/>
    /// carrying the principal as the quantity — all in a single save.
    /// </summary>
    [Fact]
    public async Task AddDeposit_PublishesPositionChangedWithPrincipal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var portfolioId = (await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Deposit outbox portfolio" }, cancellationToken)).Value.Id;

        _saveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<AddDepositHandler>()
            .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _saveChanges.Count);
        var assetId = result.Value.AssetId;

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var asset = await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);
        Assert.Equal(AssetClass.Deposit, asset.AssetClass);
        Assert.Equal(10_000m, asset.Quantity);
        Assert.Equal(1, await verifyDb.Transactions.CountAsync(t => t.AssetId == assetId, cancellationToken));
        Assert.True(await verifyDb.Set<TermDeposit>().AnyAsync(t => t.AssetId == assetId, cancellationToken));

        var evt = Assert.Single(await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken));
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(portfolioId, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.Equal(AssetClass.Deposit, evt.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, evt.ValuationMode);
        Assert.Equal("PLN", evt.Currency);
        Assert.Equal(10_000m, evt.Quantity);
        Assert.Null(evt.InstrumentId);
        Assert.Null(evt.ManualValueAmount);
        Assert.False(evt.PortfolioIsArchived);
    }

    /// <summary>
    /// savings-accounts AC-2 (outbox half): AddSavingsAccount writes the Savings-class asset, its
    /// ordinary opening Deposit, its <see cref="SavingsAccount"/> terms and one
    /// <see cref="AssetPositionChanged"/> carrying the opening amount as the quantity — all in a single save.
    /// </summary>
    [Fact]
    public async Task AddSavingsAccount_PublishesPositionChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var portfolioId = (await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Savings outbox portfolio" }, cancellationToken)).Value.Id;

        _saveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<AddSavingsAccountHandler>()
            .HandleAsync(portfolioId, PortfolioApi.NewSavingsAccountRequest(), cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _saveChanges.Count);
        var assetId = result.Value.AssetId;

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var asset = await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);
        Assert.Equal(AssetClass.Savings, asset.AssetClass);
        Assert.Equal(10_000m, asset.Quantity);
        var opening = await verifyDb.Transactions.SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(TransactionType.Deposit, opening.Type);
        Assert.True(await verifyDb.Set<SavingsAccount>().AnyAsync(t => t.AssetId == assetId, cancellationToken));

        var evt = Assert.Single(await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken));
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(portfolioId, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.Equal(AssetClass.Savings, evt.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, evt.ValuationMode);
        Assert.Equal("PLN", evt.Currency);
        Assert.Equal(10_000m, evt.Quantity);
        Assert.Null(evt.InstrumentId);
        Assert.Null(evt.ManualValueAmount);
        Assert.False(evt.PortfolioIsArchived);
    }

    /// <summary>
    /// savings-interest-settlement AC-5 (outbox half): SettleSavingsInterest stores the settlement, adds
    /// the net-interest credit and writes one further <see cref="AssetPositionChanged"/> carrying the
    /// raised balance - all in a single save. The host clock is the real one, so January 2026 has ended.
    /// </summary>
    [Fact]
    public async Task SettleSavingsInterest_PublishesPositionChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (portfolioId, assetId) = await ArrangeInterestAccountAsync(cancellationToken);
        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleSavingsInterestHandler>().HandleAsync(
                portfolioId,
                assetId,
                new SettleSavingsInterestRequest { PeriodEnd = new DateOnly(2026, 1, 31), GrossInterest = 50.00m, Tax = 9.50m },
                cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(assetId, e.AssetId));
        Assert.Equal(10_000m, events[0].Quantity);
        Assert.Equal(10_040.50m, events[1].Quantity);
        Assert.True(events[1].Version > events[0].Version);
        Assert.Equal(AssetClass.Savings, events[1].AssetClass);
        Assert.Equal(UserId, events[1].UserId);

        Assert.Equal(10_040.50m, (await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).Quantity);
        var credit = await verifyDb.Transactions.SingleAsync(t => t.AssetId == assetId && t.Quantity == 40.50m, cancellationToken);
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(new DateOnly(2026, 1, 31), credit.Date);
        var settlement = await verifyDb.Set<SavingsInterestSettlement>().SingleAsync(s => s.AssetId == assetId, cancellationToken);
        Assert.Equal(new DateOnly(2026, 1, 1), settlement.PeriodStart);
        Assert.Equal(new DateOnly(2026, 1, 31), settlement.PeriodEnd);
        Assert.Equal(50.00m, settlement.GrossInterest);
        Assert.Equal(9.50m, settlement.Tax);
        Assert.Equal(credit.Id, settlement.TransactionId);
    }

    /// <summary>
    /// savings-interest-settlement AC-8 (outbox half): undoing the latest settlement deletes it and its
    /// credit and writes one further <see cref="AssetPositionChanged"/> carrying the restored balance.
    /// </summary>
    [Fact]
    public async Task UndoSavingsInterestSettlement_PublishesPositionChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (portfolioId, assetId) = await ArrangeInterestAccountAsync(cancellationToken);
        await using (var settle = _provider.CreateAsyncScope())
        {
            var settled = await settle.ServiceProvider.GetRequiredService<SettleSavingsInterestHandler>().HandleAsync(
                portfolioId,
                assetId,
                new SettleSavingsInterestRequest { PeriodEnd = new DateOnly(2026, 1, 31), GrossInterest = 50.00m, Tax = 9.50m },
                cancellationToken);
            Assert.True(settled.IsSuccess);
        }

        Guid settlementId;
        await using (var read = _provider.CreateAsyncScope())
        {
            settlementId = (await read.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .Set<SavingsInterestSettlement>().SingleAsync(s => s.AssetId == assetId, cancellationToken)).Id;
        }

        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<UndoSavingsInterestSettlementHandler>()
                .HandleAsync(portfolioId, assetId, settlementId, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(3, events.Count);
        Assert.Equal(10_000m, events[2].Quantity);
        Assert.True(events[2].Version > events[1].Version);
        Assert.Equal(10_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).Quantity);
        Assert.False(await verifyDb.Set<SavingsInterestSettlement>().AnyAsync(s => s.AssetId == assetId, cancellationToken));
        Assert.Equal(1, await verifyDb.Transactions.CountAsync(t => t.AssetId == assetId, cancellationToken));
    }

    /// <summary>A 5 % taxed account with an opening deposit of 10 000 on 1 January 2026, arranged through its handlers.</summary>
    private async Task<(Guid PortfolioId, Guid AssetId)> ArrangeInterestAccountAsync(CancellationToken cancellationToken)
    {
        await using var arrange = _provider.CreateAsyncScope();
        var portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Interest outbox portfolio" }, cancellationToken)).Value.Id;
        var added = await arrange.ServiceProvider.GetRequiredService<AddSavingsAccountHandler>().HandleAsync(
            portfolioId,
            PortfolioApi.NewSavingsAccountRequest(annualInterestRatePercent: 5m, openingDate: new DateOnly(2026, 1, 1)),
            cancellationToken);
        Assert.True(added.IsSuccess);

        return (portfolioId, added.Value.AssetId);
    }

    /// <summary>
    /// savings-accounts AC-2: without an opening deposit the account still publishes its position
    /// (quantity 0) once, and writes no transaction.
    /// </summary>
    [Fact]
    public async Task AddSavingsAccount_WithoutOpeningDeposit_PublishesPositionChangedWithZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var portfolioId = (await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Empty savings outbox portfolio" }, cancellationToken)).Value.Id;

        var result = await scope.ServiceProvider.GetRequiredService<AddSavingsAccountHandler>()
            .HandleAsync(portfolioId, PortfolioApi.NewSavingsAccountRequest(withOpeningDeposit: false), cancellationToken);

        Assert.True(result.IsSuccess);
        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.False(await verifyDb.Transactions.AnyAsync(t => t.AssetId == result.Value.AssetId, cancellationToken));
        var evt = Assert.Single(await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken));
        Assert.Equal(AssetClass.Savings, evt.AssetClass);
        Assert.Equal(0m, evt.Quantity);
    }

    /// <summary>
    /// savings-accounts AC-4: UpdateSavingsAccount changes only terms nothing downstream carries, so it
    /// writes no event beyond the one AddSavingsAccount wrote.
    /// </summary>
    [Fact]
    public async Task UpdateSavingsAccount_PublishesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Update savings outbox portfolio" }, cancellationToken)).Value.Id;
            var added = await arrange.ServiceProvider.GetRequiredService<AddSavingsAccountHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewSavingsAccountRequest(), cancellationToken);
            Assert.True(added.IsSuccess);
            assetId = added.Value.AssetId;
        }

        int outboxRowsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            outboxRowsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<UpdateSavingsAccountHandler>()
                .HandleAsync(portfolioId, assetId, PortfolioApi.NewUpdateSavingsAccountRequest(), cancellationToken);
            Assert.True(result.IsSuccess);
        }

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        var terms = await verifyDb.Set<SavingsAccount>().SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(3m, terms.AnnualInterestRatePercent);
    }

    /// <summary>
    /// term-deposits AC-8 (outbox half): UpdateDeposit rewrites the opening transaction and the
    /// quantity and writes exactly one further <see cref="AssetPositionChanged"/> with the new
    /// quantity and a higher version, in a single save.
    /// </summary>
    [Fact]
    public async Task UpdateDeposit_PublishesOnePositionChangedWithNewQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Deposit outbox portfolio" }, cancellationToken)).Value.Id;
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess);
            assetId = added.Value.AssetId;
        }

        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var request = PortfolioApi.NewDepositRequest(principal: 15_000m, startDate: new DateOnly(2026, 2, 1), termLength: 6)
                .ToUpdateRequest();
            var result = await act.ServiceProvider.GetRequiredService<UpdateDepositHandler>()
                .HandleAsync(portfolioId, assetId, request, cancellationToken);
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(assetId, e.AssetId));
        Assert.Equal(10_000m, events[0].Quantity);
        Assert.Equal(15_000m, events[1].Quantity);
        Assert.True(events[1].Version > events[0].Version);
        Assert.Equal(AssetClass.Deposit, events[1].AssetClass);

        var opening = await verifyDb.Transactions.SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(15_000m, opening.Quantity);
        Assert.Equal(new DateOnly(2026, 2, 1), opening.Date);
        Assert.Equal(15_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).Quantity);
    }

    /// <summary>
    /// term-deposits-settlement AC-3 (outbox half): SettleDeposit stores the settlement, adds the
    /// net-interest Deposit transaction, raises the quantity to principal + net and writes exactly one
    /// further <see cref="AssetPositionChanged"/> carrying that final quantity — all in a single save.
    /// The host clock is the real one (today is well past the 2026-04-15 maturity).
    /// </summary>
    [Fact]
    public async Task SettleDeposit_PublishesPositionChangedWithFinalAmount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Settlement outbox portfolio" }, cancellationToken)).Value.Id;
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess);
            assetId = added.Value.AssetId;
        }

        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(portfolioId, assetId, PortfolioApi.NewSettleRequest(), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(assetId, e.AssetId));
        Assert.Equal(10_000m, events[0].Quantity);
        Assert.Equal(10_119.83m, events[1].Quantity);
        Assert.True(events[1].Version > events[0].Version);
        Assert.Equal(AssetClass.Deposit, events[1].AssetClass);
        Assert.Equal(UserId, events[1].UserId);

        Assert.Equal(10_119.83m, (await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).Quantity);
        var credit = await verifyDb.Transactions.SingleAsync(t => t.AssetId == assetId && t.Quantity == 119.83m, cancellationToken);
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(new DateOnly(2026, 4, 15), credit.Date);
        var terms = await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(new DateOnly(2026, 4, 15), terms.SettledOn);
        Assert.Equal(147.95m, terms.SettledGrossInterest);
        Assert.Equal(28.12m, terms.SettledTax);
    }

    /// <summary>
    /// term-deposits AC-11 (outbox half): RemoveAsset on a term deposit deletes the asset, its
    /// opening transaction and its <see cref="TermDeposit"/> row and writes one non-cascaded
    /// <see cref="AssetRemoved"/>, all in one save.
    /// </summary>
    [Fact]
    public async Task RemoveAsset_TermDeposit_WritesAssetRemovedAndDeletesTermsInOneSave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Deposit outbox portfolio" }, cancellationToken)).Value.Id;
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess);
            assetId = added.Value.AssetId;
        }

        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
                .HandleAsync(portfolioId, assetId, cancellationToken);
            Assert.True(result.IsSuccess);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.False(await verifyDb.Assets.AnyAsync(a => a.Id == assetId, cancellationToken));
        Assert.False(await verifyDb.Transactions.AnyAsync(t => t.AssetId == assetId, cancellationToken));
        Assert.False(await verifyDb.Set<TermDeposit>().AnyAsync(t => t.AssetId == assetId, cancellationToken));

        var evt = Assert.Single(await verifyDb.ReadPublishedAsync<AssetRemoved>(cancellationToken));
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(portfolioId, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.False(evt.CascadedFromPortfolio);
    }

    /// <summary>
    /// term-deposits AC-7 (outbox half): AddDeposit, UpdateDeposit and (term-deposits-settlement)
    /// SettleDeposit into an archived portfolio
    /// fail with <c>Conflict.PortfolioArchived</c> before writing anything — no outbox row, no terms change.
    /// </summary>
    [Fact]
    public async Task DepositWriteToArchivedPortfolio_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Archived deposit portfolio" }, cancellationToken)).Value.Id;
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess);
            assetId = added.Value.AssetId;
            Assert.True((await arrange.ServiceProvider.GetRequiredService<ArchivePortfolioHandler>()
                .HandleAsync(portfolioId, cancellationToken)).IsSuccess);
        }

        int outboxRowsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            outboxRowsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        var writes = new (string Name, Func<IServiceProvider, Task<Error>> Write)[]
        {
            ("AddDeposit", async s => (await s.GetRequiredService<AddDepositHandler>().HandleAsync(
                portfolioId, PortfolioApi.NewDepositRequest(name: "Another"), cancellationToken)).Error),
            ("UpdateDeposit", async s => (await s.GetRequiredService<UpdateDepositHandler>().HandleAsync(
                portfolioId,
                assetId,
                PortfolioApi.NewDepositRequest(principal: 99_999m).ToUpdateRequest(),
                cancellationToken)).Error),
            ("SettleDeposit", async s => (await s.GetRequiredService<SettleDepositHandler>().HandleAsync(
                portfolioId, assetId, PortfolioApi.NewSettleRequest(), cancellationToken)).Error),
        };

        foreach (var (name, write) in writes)
        {
            await using var scope = _provider.CreateAsyncScope();
            var error = await write(scope.ServiceProvider);
            Assert.True(
                error.Code == PortfolioAssertions.PortfolioArchivedErrorCode,
                $"{name} on an archived portfolio returned '{error.Code}', expected '{PortfolioAssertions.PortfolioArchivedErrorCode}'.");
        }

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(1, await verifyDb.Assets.CountAsync(a => a.PortfolioId == portfolioId, cancellationToken));
        Assert.Equal(10_000m, (await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == assetId, cancellationToken)).Principal);
    }

    /// <summary>
    /// asset-transfers-deposit-funding AC-3 (outbox half): a deposit funded from Cash in another
    /// portfolio writes both legs and one <see cref="AssetPositionChanged"/> per asset — Cash at 4 000,
    /// the deposit at 1 000 — all in a single save.
    /// </summary>
    [Fact]
    public async Task AddFundedDeposit_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid walletId;
        Guid cashId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            walletId = await CreatePortfolioAsync(arrange.ServiceProvider, "Wallet", cancellationToken);
            cashId = await AddCashWithBalanceAsync(arrange.ServiceProvider, walletId, 5_000m, cancellationToken);
            savingsId = await CreatePortfolioAsync(arrange.ServiceProvider, "Savings", cancellationToken);
        }

        int positionEventsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            positionEventsBefore = (await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count;
        }

        _saveChanges.Reset();

        Guid depositId;
        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<AddDepositHandler>().HandleAsync(
                savingsId, PortfolioApi.NewDepositRequest(principal: 1_000m, fundingAssetId: cashId), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
            depositId = result.Value.AssetId;
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(positionEventsBefore).ToList();
        Assert.Equal(2, events.Count);
        var cashEvent = Assert.Single(events, e => e.AssetId == cashId);
        Assert.Equal(4_000m, cashEvent.Quantity);
        Assert.Equal(walletId, cashEvent.PortfolioId);
        Assert.Equal(AssetClass.Cash, cashEvent.AssetClass);
        Assert.Equal(UserId, cashEvent.UserId);
        var depositEvent = Assert.Single(events, e => e.AssetId == depositId);
        Assert.Equal(1_000m, depositEvent.Quantity);
        Assert.Equal(savingsId, depositEvent.PortfolioId);
        Assert.Equal(AssetClass.Deposit, depositEvent.AssetClass);

        var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.Equal(4_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == cashId, cancellationToken)).Quantity);
        Assert.Equal(1_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == depositId, cancellationToken)).Quantity);
    }

    /// <summary>
    /// asset-transfers-deposit-funding AC-5 (outbox half): UpdateDeposit on a funded deposit rewrites
    /// both legs and writes one further <see cref="AssetPositionChanged"/> per asset — Cash at 3 500,
    /// the deposit at 1 500, each at a higher version — in a single save.
    /// </summary>
    [Fact]
    public async Task UpdateFundedDeposit_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid cashId;
        Guid depositId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            var walletId = await CreatePortfolioAsync(arrange.ServiceProvider, "Wallet", cancellationToken);
            cashId = await AddCashWithBalanceAsync(arrange.ServiceProvider, walletId, 5_000m, cancellationToken);
            savingsId = await CreatePortfolioAsync(arrange.ServiceProvider, "Savings", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>().HandleAsync(
                savingsId, PortfolioApi.NewDepositRequest(principal: 1_000m, fundingAssetId: cashId), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            depositId = added.Value.AssetId;
        }

        IReadOnlyList<AssetPositionChanged> eventsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            eventsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        }

        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var request = PortfolioApi.NewDepositRequest(principal: 1_500m, startDate: new DateOnly(2026, 2, 1)).ToUpdateRequest();
            var result = await act.ServiceProvider.GetRequiredService<UpdateDepositHandler>()
                .HandleAsync(savingsId, depositId, request, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore.Count).ToList();
        Assert.Equal(2, events.Count);
        var cashEvent = Assert.Single(events, e => e.AssetId == cashId);
        Assert.Equal(3_500m, cashEvent.Quantity);
        Assert.True(cashEvent.Version > eventsBefore.Where(e => e.AssetId == cashId).Max(e => e.Version));
        var depositEvent = Assert.Single(events, e => e.AssetId == depositId);
        Assert.Equal(1_500m, depositEvent.Quantity);
        Assert.True(depositEvent.Version > eventsBefore.Where(e => e.AssetId == depositId).Max(e => e.Version));

        var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.All(legs, leg => Assert.Equal(1_500m, leg.Quantity));
        Assert.All(legs, leg => Assert.Equal(new DateOnly(2026, 2, 1), leg.Date));
    }

    /// <summary>
    /// asset-transfers-deposit-funding AC-7 (outbox half): removing a funded deposit writes its
    /// <see cref="AssetRemoved"/> and detaches the Cash leg in the same save — but publishes nothing
    /// for the Cash asset, whose quantity does not change.
    /// </summary>
    [Fact]
    public async Task RemoveFundedDeposit_DetachesCashLegWithoutPublishingForIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid cashId;
        Guid depositId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            var walletId = await CreatePortfolioAsync(arrange.ServiceProvider, "Wallet", cancellationToken);
            cashId = await AddCashWithBalanceAsync(arrange.ServiceProvider, walletId, 5_000m, cancellationToken);
            savingsId = await CreatePortfolioAsync(arrange.ServiceProvider, "Savings", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>().HandleAsync(
                savingsId, PortfolioApi.NewDepositRequest(principal: 1_000m, fundingAssetId: cashId), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            depositId = added.Value.AssetId;
        }

        int positionEventsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            positionEventsBefore = (await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count;
        }

        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
                .HandleAsync(savingsId, depositId, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(depositId, Assert.Single(await verifyDb.ReadPublishedAsync<AssetRemoved>(cancellationToken)).AssetId);
        Assert.Equal(positionEventsBefore, (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count);
        var cashLeg = await verifyDb.Transactions.SingleAsync(t => t.AssetId == cashId && t.Type == TransactionType.Withdraw, cancellationToken);
        Assert.Null(cashLeg.TransferId);
        Assert.Equal(4_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == cashId, cancellationToken)).Quantity);
    }

    /// <summary>
    /// deposit-payout-to-cash AC-1 (outbox half): settling with a destination stores the settlement,
    /// the net-interest credit and both transfer legs, and writes exactly one further
    /// <see cref="AssetPositionChanged"/> per asset carrying its final quantity — the deposit at 0, the
    /// Cash at 5 000 + 10 119.83 — all in a single save. The host clock is the real one (today is well
    /// past the 2026-04-15 maturity).
    /// </summary>
    [Fact]
    public async Task SettleDepositWithDestination_PublishesBothPositions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid walletId;
        Guid cashId;
        Guid depositId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            walletId = await CreatePortfolioAsync(arrange.ServiceProvider, "Wallet", cancellationToken);
            cashId = await AddCashWithBalanceAsync(arrange.ServiceProvider, walletId, 5_000m, cancellationToken);
            savingsId = await CreatePortfolioAsync(arrange.ServiceProvider, "Savings", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(savingsId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            depositId = added.Value.AssetId;
        }

        IReadOnlyList<AssetPositionChanged> eventsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            eventsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        }

        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(savingsId, depositId, PortfolioApi.NewSettleRequest(destinationAssetId: cashId), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore.Count).ToList();
        Assert.Equal(2, events.Count);
        var depositEvent = Assert.Single(events, e => e.AssetId == depositId);
        Assert.Equal(0m, depositEvent.Quantity);
        Assert.Equal(savingsId, depositEvent.PortfolioId);
        Assert.Equal(AssetClass.Deposit, depositEvent.AssetClass);
        Assert.Equal(UserId, depositEvent.UserId);
        Assert.True(depositEvent.Version > eventsBefore.Where(e => e.AssetId == depositId).Max(e => e.Version));
        var cashEvent = Assert.Single(events, e => e.AssetId == cashId);
        Assert.Equal(15_119.83m, cashEvent.Quantity);
        Assert.Equal(walletId, cashEvent.PortfolioId);
        Assert.Equal(AssetClass.Cash, cashEvent.AssetClass);
        Assert.True(cashEvent.Version > eventsBefore.Where(e => e.AssetId == cashId).Max(e => e.Version));

        Assert.Equal(0m, (await verifyDb.Assets.SingleAsync(a => a.Id == depositId, cancellationToken)).Quantity);
        Assert.Equal(15_119.83m, (await verifyDb.Assets.SingleAsync(a => a.Id == cashId, cancellationToken)).Quantity);
        var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.All(legs, leg => Assert.Equal(10_119.83m, leg.Quantity));
        Assert.All(legs, leg => Assert.Equal(new DateOnly(2026, 4, 15), leg.Date));
        var terms = await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == depositId, cancellationToken);
        Assert.Equal(new DateOnly(2026, 4, 15), terms.SettledOn);
    }

    /// <summary>
    /// deposit-payout-to-savings AC-2 (outbox half): settling into an empty PLN savings account writes
    /// exactly one further <see cref="AssetPositionChanged"/> per asset carrying its final quantity - the
    /// deposit at 0, the savings account at 10 119.83 - in a single save.
    /// </summary>
    [Fact]
    public async Task SettleDepositIntoSavings_PublishesBothPositions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid depositPortfolioId;
        Guid depositId;
        Guid savingsPortfolioId;
        Guid savingsId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            savingsPortfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Wallet", cancellationToken);
            var account = await arrange.ServiceProvider.GetRequiredService<AddSavingsAccountHandler>()
                .HandleAsync(savingsPortfolioId, PortfolioApi.NewSavingsAccountRequest(withOpeningDeposit: false), cancellationToken);
            Assert.True(account.IsSuccess, account.IsFailure ? account.Error.Code : null);
            savingsId = account.Value.AssetId;
            depositPortfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Deposits", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(depositPortfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            depositId = added.Value.AssetId;
        }

        var eventsBefore = await CountPositionEventsAsync(cancellationToken);
        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(depositPortfolioId, depositId, PortfolioApi.NewSettleRequest(destinationAssetId: savingsId), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore).ToList();
        Assert.Equal(2, events.Count);
        var depositEvent = Assert.Single(events, e => e.AssetId == depositId);
        Assert.Equal(0m, depositEvent.Quantity);
        Assert.Equal(AssetClass.Deposit, depositEvent.AssetClass);
        var savingsEvent = Assert.Single(events, e => e.AssetId == savingsId);
        Assert.Equal(10_119.83m, savingsEvent.Quantity);
        Assert.Equal(savingsPortfolioId, savingsEvent.PortfolioId);
        Assert.Equal(AssetClass.Savings, savingsEvent.AssetClass);
        var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.Equal(10_119.83m, (await verifyDb.Assets.SingleAsync(a => a.Id == savingsId, cancellationToken)).Quantity);
    }

    /// <summary>
    /// deposit-payout-to-cash AC-2 (outbox half): settle and payout are atomic — an invalid destination
    /// (here a Stock) fails with <c>Validation.InvalidTransferCounterpart</c> before anything is saved:
    /// no outbox row, no settlement, no transaction.
    /// </summary>
    [Fact]
    public async Task SettleDepositWithInvalidDestination_WritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid stockId;
        Guid depositId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            var brokerId = await CreatePortfolioAsync(arrange.ServiceProvider, "Broker", cancellationToken);
            stockId = await AddAssetWithBuyAsync(arrange.ServiceProvider, brokerId, "Some stock", cancellationToken);
            savingsId = await CreatePortfolioAsync(arrange.ServiceProvider, "Savings", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(savingsId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            depositId = added.Value.AssetId;
        }

        int outboxRowsBefore;
        int transactionsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            var beforeDb = before.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            outboxRowsBefore = await beforeDb.Set<OutboxMessage>().CountAsync(cancellationToken);
            transactionsBefore = await beforeDb.Transactions.CountAsync(cancellationToken);
        }

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(savingsId, depositId, PortfolioApi.NewSettleRequest(destinationAssetId: stockId), cancellationToken);
            Assert.True(result.IsFailure);
            Assert.Equal(PortfolioAssertions.InvalidTransferCounterpartErrorCode, result.Error.Code);
        }

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(transactionsBefore, await verifyDb.Transactions.CountAsync(cancellationToken));
        Assert.Null((await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == depositId, cancellationToken)).SettledOn);
        Assert.Equal(10_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == depositId, cancellationToken)).Quantity);
    }

    /// <summary>
    /// deposit-payout-to-cash AC-3 (outbox half): paying a settled deposit out later writes both legs
    /// and exactly one further <see cref="AssetPositionChanged"/> per asset — the deposit at 0, the Cash
    /// at 5 000 + 10 119.83 — in a single save; a second payout of the now paid-out deposit fails with
    /// <c>Conflict.DepositAlreadyPaidOut</c> and writes no further outbox row.
    /// </summary>
    [Fact]
    public async Task PayOutDeposit_PublishesBothPositions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid walletId;
        Guid cashId;
        Guid depositId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            walletId = await CreatePortfolioAsync(arrange.ServiceProvider, "Wallet", cancellationToken);
            cashId = await AddCashWithBalanceAsync(arrange.ServiceProvider, walletId, 5_000m, cancellationToken);
            savingsId = await CreatePortfolioAsync(arrange.ServiceProvider, "Savings", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(savingsId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            depositId = added.Value.AssetId;
            var settled = await arrange.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(savingsId, depositId, PortfolioApi.NewSettleRequest(), cancellationToken);
            Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        }

        IReadOnlyList<AssetPositionChanged> eventsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            eventsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        }

        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<PayOutDepositHandler>()
                .HandleAsync(savingsId, depositId, PortfolioApi.NewPayOutRequest(cashId), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        int outboxRowsAfterPayOut;
        await using (var verify = _provider.CreateAsyncScope())
        {
            var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore.Count).ToList();
            Assert.Equal(2, events.Count);
            var depositEvent = Assert.Single(events, e => e.AssetId == depositId);
            Assert.Equal(0m, depositEvent.Quantity);
            Assert.Equal(AssetClass.Deposit, depositEvent.AssetClass);
            Assert.True(depositEvent.Version > eventsBefore.Where(e => e.AssetId == depositId).Max(e => e.Version));
            var cashEvent = Assert.Single(events, e => e.AssetId == cashId);
            Assert.Equal(15_119.83m, cashEvent.Quantity);
            Assert.Equal(walletId, cashEvent.PortfolioId);
            Assert.True(cashEvent.Version > eventsBefore.Where(e => e.AssetId == cashId).Max(e => e.Version));

            var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
            Assert.Equal(2, legs.Count);
            Assert.All(legs, leg => Assert.Equal(10_119.83m, leg.Quantity));
            Assert.All(legs, leg => Assert.Equal(new DateOnly(2026, 4, 18), leg.Date));
            outboxRowsAfterPayOut = await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        await using (var again = _provider.CreateAsyncScope())
        {
            var second = await again.ServiceProvider.GetRequiredService<PayOutDepositHandler>()
                .HandleAsync(savingsId, depositId, PortfolioApi.NewPayOutRequest(cashId), cancellationToken);
            Assert.True(second.IsFailure);
            Assert.Equal(PortfolioAssertions.DepositAlreadyPaidOutErrorCode, second.Error.Code);
        }

        await using var final = _provider.CreateAsyncScope();
        var finalDb = final.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsAfterPayOut, await finalDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(15_119.83m, (await finalDb.Assets.SingleAsync(a => a.Id == cashId, cancellationToken)).Quantity);
    }

    /// <summary>
    /// deposit-rollover AC-1 (outbox half): rolling a Due deposit over settles it and starts the next
    /// term in a single save — the net-interest credit, the new terms and exactly one further
    /// <see cref="AssetPositionChanged"/> for the deposit, carrying 10 119.83. The host clock is the real
    /// one (today is well past the 2026-04-15 maturity).
    /// </summary>
    [Fact]
    public async Task RollOverDueDeposit_PublishesPosition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            portfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Rollover outbox portfolio", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            assetId = added.Value.AssetId;
        }

        IReadOnlyList<AssetPositionChanged> eventsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            eventsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        }

        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<RollOverDepositHandler>()
                .HandleAsync(portfolioId, assetId, PortfolioApi.NewRollOverRequest(), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore.Count).ToList();
        var evt = Assert.Single(events);
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(10_119.83m, evt.Quantity);
        Assert.Equal(AssetClass.Deposit, evt.AssetClass);
        Assert.Equal(UserId, evt.UserId);
        Assert.True(evt.Version > eventsBefore.Where(e => e.AssetId == assetId).Max(e => e.Version));

        Assert.Equal(10_119.83m, (await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).Quantity);
        var credit = await verifyDb.Transactions.SingleAsync(t => t.AssetId == assetId && t.Quantity == 119.83m, cancellationToken);
        Assert.Equal(TransactionType.Deposit, credit.Type);
        Assert.Equal(new DateOnly(2026, 4, 15), credit.Date);
        var terms = await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(1, terms.RolloverCount);
        Assert.Equal(10_119.83m, terms.Principal);
        Assert.Equal(new DateOnly(2026, 4, 15), terms.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 15), terms.MaturityDate);
        Assert.Equal(5.5m, terms.AnnualInterestRatePercent);
        Assert.Null(terms.SettledOn);
        Assert.Null(terms.SettledGrossInterest);
        Assert.Null(terms.SettledTax);
    }

    /// <summary>
    /// deposit-rollover AC-2 (outbox half): rolling a Settled deposit over changes only its terms — the
    /// quantity stays, so the save writes no outbox row at all.
    /// </summary>
    [Fact]
    public async Task RollOverSettledDeposit_PublishesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            portfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Rollover outbox portfolio", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewDepositRequest(), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            assetId = added.Value.AssetId;
            var settled = await arrange.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(portfolioId, assetId, PortfolioApi.NewSettleRequest(), cancellationToken);
            Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        }

        int outboxRowsBefore;
        int transactionsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            var beforeDb = before.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            outboxRowsBefore = await beforeDb.Set<OutboxMessage>().CountAsync(cancellationToken);
            transactionsBefore = await beforeDb.Transactions.CountAsync(cancellationToken);
        }

        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<RollOverDepositHandler>()
                .HandleAsync(portfolioId, assetId, PortfolioApi.NewRollOverRequest(grossInterest: null, tax: null), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(transactionsBefore, await verifyDb.Transactions.CountAsync(cancellationToken));
        Assert.Equal(10_119.83m, (await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).Quantity);
        var terms = await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(1, terms.RolloverCount);
        Assert.Equal(10_119.83m, terms.Principal);
        Assert.Equal(new DateOnly(2026, 4, 15), terms.StartDate);
        Assert.Null(terms.SettledOn);
    }

    /// <summary>asset-archive AC-1: archiving writes exactly one <see cref="AssetPositionChanged"/> with <c>IsArchived = true</c> and a higher <c>Version</c>, in the same save as the flag.</summary>
    [Fact]
    public async Task ArchiveAsset_PublishesPositionChangedWithArchivedFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Outbox test portfolio", cancellationToken);
        var assetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 100m, cancellationToken);
        var before = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var versionBefore = before.Where(e => e.AssetId == assetId).Max(e => e.Version);
        _saveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<ArchiveAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, _saveChanges.Count);
        var after = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(before.Count + 1, after.Count);
        var evt = after[^1];
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(portfolioId, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.True(evt.IsArchived);
        Assert.False(evt.PortfolioIsArchived);
        Assert.Equal(100m, evt.Quantity);
        Assert.True(evt.Version > versionBefore);
    }

    /// <summary>asset-archive AC-1: a second archive is a success that writes no further event.</summary>
    [Fact]
    public async Task ArchiveAsset_AlreadyArchived_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Outbox test portfolio", cancellationToken);
        var assetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 100m, cancellationToken);
        var handler = scope.ServiceProvider.GetRequiredService<ArchiveAssetHandler>();
        Assert.True((await handler.HandleAsync(portfolioId, assetId, cancellationToken)).IsSuccess);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var second = await handler.HandleAsync(portfolioId, assetId, cancellationToken);

        Assert.True(second.IsSuccess);
        Assert.True(second.Value.IsArchived);
        Assert.Equal(outboxRowsBefore, await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken));
    }

    /// <summary>asset-archive AC-2: restoring writes one <see cref="AssetPositionChanged"/> with <c>IsArchived = false</c> and a higher <c>Version</c>.</summary>
    [Fact]
    public async Task RestoreAsset_PublishesPositionChangedWithoutArchivedFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Outbox test portfolio", cancellationToken);
        var assetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 100m, cancellationToken);
        Assert.True((await scope.ServiceProvider.GetRequiredService<ArchiveAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken)).IsSuccess);
        var before = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var versionBefore = before.Where(e => e.AssetId == assetId).Max(e => e.Version);
        _saveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<RestoreAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, _saveChanges.Count);
        var after = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(before.Count + 1, after.Count);
        var evt = after[^1];
        Assert.Equal(assetId, evt.AssetId);
        Assert.False(evt.IsArchived);
        Assert.False(evt.PortfolioIsArchived);
        Assert.True(evt.Version > versionBefore);
    }

    /// <summary>asset-archive AC-2: restoring an asset that is not archived writes no event.</summary>
    [Fact]
    public async Task RestoreAsset_NotArchived_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Outbox test portfolio", cancellationToken);
        var assetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 100m, cancellationToken);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var result = await scope.ServiceProvider.GetRequiredService<RestoreAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsArchived);
        Assert.Equal(outboxRowsBefore, await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken));
    }

    /// <summary>asset-archive AC-3: archive and restore in an archived portfolio, or on an unknown asset, fail without an event or a flag change.</summary>
    [Fact]
    public async Task ArchiveAndRestoreAsset_ArchivedPortfolioOrUnknownAsset_WriteNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            portfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Archived outbox portfolio", cancellationToken);
            assetId = await AddCashWithBalanceAsync(arrange.ServiceProvider, portfolioId, 100m, cancellationToken);
            Assert.True((await arrange.ServiceProvider.GetRequiredService<ArchivePortfolioHandler>()
                .HandleAsync(portfolioId, cancellationToken)).IsSuccess);
        }

        int outboxRowsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            outboxRowsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        var calls = new (string Name, Func<IServiceProvider, Task<Error>> Call, string ExpectedCode)[]
        {
            ("Archive", async s => (await s.GetRequiredService<ArchiveAssetHandler>().HandleAsync(portfolioId, assetId, cancellationToken)).Error, PortfolioAssertions.PortfolioArchivedErrorCode),
            ("Restore", async s => (await s.GetRequiredService<RestoreAssetHandler>().HandleAsync(portfolioId, assetId, cancellationToken)).Error, PortfolioAssertions.PortfolioArchivedErrorCode),
            ("Archive unknown", async s => (await s.GetRequiredService<ArchiveAssetHandler>().HandleAsync(portfolioId, Guid.NewGuid(), cancellationToken)).Error, "NotFound"),
        };

        foreach (var (name, call, expectedCode) in calls)
        {
            await using var scope = _provider.CreateAsyncScope();
            var error = await call(scope.ServiceProvider);
            Assert.True(
                error.Code.StartsWith(expectedCode, StringComparison.Ordinal),
                $"{name} returned '{error.Code}', expected a code starting '{expectedCode}'.");
        }

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.False((await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken)).IsArchived);
    }

    /// <summary>
    /// asset-archive AC-4: every write the archived asset refuses — UpdateAsset, RecordTransaction,
    /// UpdateTransaction, DeleteTransaction — answers <c>Conflict.AssetArchived</c> and adds not one outbox
    /// row, while RemoveAsset still deletes it and publishes <see cref="AssetRemoved"/>. Each write runs in
    /// its own scope, as in <see cref="WriteToArchivedPortfolio_WritesNoEvent"/>.
    /// </summary>
    [Fact]
    public async Task WriteToArchivedAsset_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = _provider.CreateAsyncScope())
        {
            portfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Archived asset portfolio", cancellationToken);
            assetId = await AddCashWithBalanceAsync(arrange.ServiceProvider, portfolioId, 100m, cancellationToken);
            Assert.True((await arrange.ServiceProvider.GetRequiredService<ArchiveAssetHandler>()
                .HandleAsync(portfolioId, assetId, cancellationToken)).IsSuccess);
        }

        Guid transactionId;
        int outboxRowsBefore;
        await using (var before = _provider.CreateAsyncScope())
        {
            var db = before.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            transactionId = await db.Transactions.Where(t => t.AssetId == assetId).Select(t => t.Id).SingleAsync(cancellationToken);
            outboxRowsBefore = await db.Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        var writes = new (string Name, Func<IServiceProvider, Task<Error>> Write)[]
        {
            ("UpdateAsset", async s => (await s.GetRequiredService<UpdateAssetHandler>().HandleAsync(
                portfolioId,
                assetId,
                new UpdateAssetRequest { AssetClass = AssetClass.Cash, Name = "Renamed", Currency = "PLN" },
                cancellationToken)).Error),
            ("RecordTransaction", async s => (await s.GetRequiredService<RecordTransactionHandler>().HandleAsync(
                portfolioId,
                assetId,
                new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 3) },
                cancellationToken)).Error),
            ("UpdateTransaction", async s => (await s.GetRequiredService<UpdateTransactionHandler>().HandleAsync(
                portfolioId,
                assetId,
                transactionId,
                new UpdateTransactionRequest { Type = TransactionType.Deposit, Quantity = 50m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 2) },
                cancellationToken)).Error),
            ("DeleteTransaction", async s => (await s.GetRequiredService<DeleteTransactionHandler>()
                .HandleAsync(portfolioId, assetId, transactionId, cancellationToken)).Error),
        };

        foreach (var (name, write) in writes)
        {
            await using var scope = _provider.CreateAsyncScope();
            var error = await write(scope.ServiceProvider);
            Assert.True(
                error.Code == PortfolioAssertions.AssetArchivedErrorCode,
                $"{name} on an archived asset returned '{error.Code}', expected '{PortfolioAssertions.AssetArchivedErrorCode}'.");
        }

        await using (var verify = _provider.CreateAsyncScope())
        {
            var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
            var asset = await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);
            Assert.Equal("Cash account", asset.Name);
            Assert.Equal(100m, asset.Quantity);
            Assert.Equal(100m, (await verifyDb.Transactions.SingleAsync(t => t.Id == transactionId, cancellationToken)).Quantity);
        }

        await using var remove = _provider.CreateAsyncScope();
        var removeResult = await remove.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
            .HandleAsync(portfolioId, assetId, cancellationToken);

        Assert.True(removeResult.IsSuccess, removeResult.IsFailure ? removeResult.Error.Code : null);
        var removeDb = remove.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.False(await removeDb.Assets.AnyAsync(a => a.Id == assetId, cancellationToken));
        Assert.Equal(assetId, Assert.Single(await removeDb.ReadPublishedAsync<AssetRemoved>(cancellationToken)).AssetId);
    }

    /// <summary>asset-archive AC-8: the portfolio archive fan-out carries <c>PortfolioIsArchived = true</c> and each asset's own flag, unchanged.</summary>
    [Fact]
    public async Task ArchivePortfolio_KeepsAssetArchivedFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Outbox test portfolio", cancellationToken);
        var archivedAssetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 100m, cancellationToken);
        var liveAssetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 200m, cancellationToken);
        Assert.True((await scope.ServiceProvider.GetRequiredService<ArchiveAssetHandler>()
            .HandleAsync(portfolioId, archivedAssetId, cancellationToken)).IsSuccess);

        var result = await scope.ServiceProvider.GetRequiredService<ArchivePortfolioHandler>()
            .HandleAsync(portfolioId, cancellationToken);

        Assert.True(result.IsSuccess);
        var fanOut = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken))
            .Where(e => e.PortfolioIsArchived)
            .ToList();
        Assert.Equal(2, fanOut.Count);
        Assert.True(fanOut.Single(e => e.AssetId == archivedAssetId).IsArchived);
        Assert.False(fanOut.Single(e => e.AssetId == liveAssetId).IsArchived);
    }

    /// <summary>asset-archive AC-8: restoring the portfolio leaves an asset archived on its own archived, on the event and in the row.</summary>
    [Fact]
    public async Task RestorePortfolio_KeepsAssetArchivedFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = _provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Outbox test portfolio", cancellationToken);
        var archivedAssetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 100m, cancellationToken);
        var liveAssetId = await AddCashWithBalanceAsync(scope.ServiceProvider, portfolioId, 200m, cancellationToken);
        Assert.True((await scope.ServiceProvider.GetRequiredService<ArchiveAssetHandler>()
            .HandleAsync(portfolioId, archivedAssetId, cancellationToken)).IsSuccess);
        Assert.True((await scope.ServiceProvider.GetRequiredService<ArchivePortfolioHandler>()
            .HandleAsync(portfolioId, cancellationToken)).IsSuccess);
        var eventsBefore = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count;

        var result = await scope.ServiceProvider.GetRequiredService<RestorePortfolioHandler>()
            .HandleAsync(portfolioId, cancellationToken);

        Assert.True(result.IsSuccess);
        var fanOut = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore).ToList();
        Assert.Equal(2, fanOut.Count);
        Assert.All(fanOut, e => Assert.False(e.PortfolioIsArchived));
        Assert.True(fanOut.Single(e => e.AssetId == archivedAssetId).IsArchived);
        Assert.False(fanOut.Single(e => e.AssetId == liveAssetId).IsArchived);
        Assert.True((await dbContext.Assets.SingleAsync(a => a.Id == archivedAssetId, cancellationToken)).IsArchived);
    }

    /// <summary>
    /// savings-cash-transfers AC-2 (outbox half): a Cash → Savings transfer writes both legs and one
    /// <see cref="AssetPositionChanged"/> per asset - Cash at 3 000, the savings account at 2 000 - in a
    /// single save.
    /// </summary>
    [Fact]
    public async Task CreateTransfer_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var (walletId, cashId, savingsId, savingsAssetId) = await ArrangeCashAndSavingsAsync(cancellationToken);
        var positionEventsBefore = await CountPositionEventsAsync(cancellationToken);
        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<CreateTransferHandler>()
                .HandleAsync(PortfolioApi.NewTransferRequest(cashId, savingsAssetId, 2_000m), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(positionEventsBefore).ToList();
        Assert.Equal(2, events.Count);
        var cashEvent = Assert.Single(events, e => e.AssetId == cashId);
        Assert.Equal(3_000m, cashEvent.Quantity);
        Assert.Equal(walletId, cashEvent.PortfolioId);
        Assert.Equal(UserId, cashEvent.UserId);
        var savingsEvent = Assert.Single(events, e => e.AssetId == savingsAssetId);
        Assert.Equal(2_000m, savingsEvent.Quantity);
        Assert.Equal(savingsId, savingsEvent.PortfolioId);
        Assert.Equal(AssetClass.Savings, savingsEvent.AssetClass);

        var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
    }

    /// <summary>
    /// savings-cash-transfers AC-4 (outbox half): deleting the transfer removes both legs and writes one
    /// further <see cref="AssetPositionChanged"/> per asset - Cash back at 5 000, the account at 0 - in a
    /// single save.
    /// </summary>
    [Fact]
    public async Task DeleteTransfer_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var (_, cashId, _, savingsAssetId) = await ArrangeCashAndSavingsAsync(cancellationToken);
        Guid transferId;
        await using (var create = _provider.CreateAsyncScope())
        {
            var created = await create.ServiceProvider.GetRequiredService<CreateTransferHandler>()
                .HandleAsync(PortfolioApi.NewTransferRequest(cashId, savingsAssetId, 2_000m), cancellationToken);
            Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Code : null);
            transferId = created.Value.TransferId;
        }

        var positionEventsBefore = await CountPositionEventsAsync(cancellationToken);
        _saveChanges.Reset();

        await using (var act = _provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<DeleteTransferHandler>()
                .HandleAsync(transferId, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, _saveChanges.Count);

        await using var verify = _provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(positionEventsBefore).ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(5_000m, Assert.Single(events, e => e.AssetId == cashId).Quantity);
        Assert.Equal(0m, Assert.Single(events, e => e.AssetId == savingsAssetId).Quantity);
        Assert.False(await verifyDb.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }

    private async Task<(Guid WalletId, Guid CashId, Guid SavingsPortfolioId, Guid SavingsAssetId)> ArrangeCashAndSavingsAsync(
        CancellationToken cancellationToken)
    {
        await using var arrange = _provider.CreateAsyncScope();
        var walletId = await CreatePortfolioAsync(arrange.ServiceProvider, "Wallet", cancellationToken);
        var cashId = await AddCashWithBalanceAsync(arrange.ServiceProvider, walletId, 5_000m, cancellationToken);
        var savingsPortfolioId = await CreatePortfolioAsync(arrange.ServiceProvider, "Savings", cancellationToken);
        var account = await arrange.ServiceProvider.GetRequiredService<AddSavingsAccountHandler>()
            .HandleAsync(savingsPortfolioId, PortfolioApi.NewSavingsAccountRequest(withOpeningDeposit: false), cancellationToken);
        Assert.True(account.IsSuccess, account.IsFailure ? account.Error.Code : null);

        return (walletId, cashId, savingsPortfolioId, account.Value.AssetId);
    }

    private async Task<int> CountPositionEventsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _provider.CreateAsyncScope();

        return (await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count;
    }

    private static async Task<Guid> CreatePortfolioAsync(IServiceProvider services, string name, CancellationToken cancellationToken)
    {
        var result = await services.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = name }, cancellationToken);
        Assert.True(result.IsSuccess);

        return result.Value.Id;
    }

    /// <summary>A PLN Cash asset in <paramref name="portfolioId"/> topped up with <paramref name="balance"/> on 2026-01-01.</summary>
    private static async Task<Guid> AddCashWithBalanceAsync(
        IServiceProvider services, Guid portfolioId, decimal balance, CancellationToken cancellationToken)
    {
        var assetResult = await services.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioId,
            new AddAssetRequest { AssetClass = AssetClass.Cash, Name = "Cash account", Currency = "PLN" },
            cancellationToken);
        Assert.True(assetResult.IsSuccess, assetResult.IsFailure ? assetResult.Error.Code : null);

        var topUp = await services.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            portfolioId,
            assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = balance, UnitPrice = 1m, Date = PortfolioApi.DefaultTopUpDate },
            cancellationToken);
        Assert.True(topUp.IsSuccess, topUp.IsFailure ? topUp.Error.Code : null);

        return assetResult.Value.Id;
    }

    private static async Task<Guid> AddAssetWithBuyAsync(
        IServiceProvider services, Guid portfolioId, string name, CancellationToken cancellationToken)
    {
        var assetResult = await services.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioId,
            new AddAssetRequest { AssetClass = AssetClass.Stock, Name = name, Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2026, 1, 1) },
            cancellationToken);
        Assert.True(assetResult.IsSuccess);

        var buyResult = await services.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            portfolioId,
            assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 5m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 2) },
            cancellationToken);
        Assert.True(buyResult.IsSuccess);

        return assetResult.Value.Id;
    }
}
