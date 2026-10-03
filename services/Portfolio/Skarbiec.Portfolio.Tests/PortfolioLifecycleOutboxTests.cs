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
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.RestorePortfolio;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class PortfolioLifecycleOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task ArchivePortfolio_WritesPortfolioArchivedAndOnePositionEventPerAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task RestorePortfolio_WritesPortfolioRestoredAndOnePositionEventPerAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task RestorePortfolio_NotArchivedPortfolio_WritesNoFurtherEvents()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task ArchivePortfolio_AlreadyArchivedPortfolio_WritesNoFurtherEvents()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task DeletePortfolio_WritesPortfolioDeletedInSameTransactionAsDeletion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task DeletePortfolio_WithAssets_RemovesChildrenAndWritesAssetRemovedPerAssetAndPortfolioDeleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
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

        SaveChanges.Reset();

        var deleteResult = await scope.ServiceProvider.GetRequiredService<DeletePortfolioHandler>()
            .HandleAsync(portfolioId, cancellationToken);

        Assert.True(deleteResult.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task WriteToArchivedPortfolio_WritesNoEvent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Archived outbox portfolio" }, cancellationToken)).Value.Id;
            assetId = await AddAssetWithBuyAsync(arrange.ServiceProvider, portfolioId, "Shares", cancellationToken);
            Assert.True((await arrange.ServiceProvider.GetRequiredService<ArchivePortfolioHandler>()
                .HandleAsync(portfolioId, cancellationToken)).IsSuccess);
        }

        Guid transactionId;
        int outboxRowsBefore;
        await using (var before = Provider.CreateAsyncScope())
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
            await using var scope = Provider.CreateAsyncScope();
            var error = await write(scope.ServiceProvider);
            Assert.True(
                error.Code == PortfolioAssertions.PortfolioArchivedErrorCode,
                $"{name} on an archived portfolio returned '{error.Code}', expected '{PortfolioAssertions.PortfolioArchivedErrorCode}'.");
        }

        await using var verify = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task ArchivePortfolio_KeepsAssetArchivedFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task RestorePortfolio_KeepsAssetArchivedFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
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
}
