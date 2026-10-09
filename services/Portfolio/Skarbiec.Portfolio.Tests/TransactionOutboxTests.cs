using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.DeleteTransaction;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransactionOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task RecordTransaction_WritesAssetPositionChangedWithRecomputedQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task UpdateTransaction_WritesAssetPositionChangedWithRecomputedQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task DeleteTransaction_WritesAssetPositionChangedWithRecomputedQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
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

        // The last event in write order is the delete's; the quantity alone is ambiguous, as the earlier Buy also left 10.
        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var deleteEvent = events[^1];
        Assert.Equal(10m, deleteEvent.Quantity);
        Assert.Equal(assetResult.Value.Id, deleteEvent.AssetId);
    }

    [Fact]
    public async Task Record_PublishesFirstTransactionDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Stock, Name = "Shares", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2024, 1, 1) },
            cancellationToken);
        var recordHandler = scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>();

        var later = await recordHandler.HandleAsync(
            portfolioResult.Value.Id, assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = new DateOnly(2025, 1, 10) },
            cancellationToken);
        Assert.True(later.IsSuccess);
        var afterFirst = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(new DateOnly(2025, 1, 10), afterFirst[^1].FirstTransactionDate);

        var earlier = await recordHandler.HandleAsync(
            portfolioResult.Value.Id, assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = new DateOnly(2024, 3, 4) },
            cancellationToken);
        Assert.True(earlier.IsSuccess);

        // A fresh scope, as in a real request: the earlier rows come from the stored-transaction query, not the change tracker.
        await using var requestScope = Provider.CreateAsyncScope();
        var extra = await requestScope.ServiceProvider.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            portfolioResult.Value.Id, assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = new DateOnly(2025, 2, 1) },
            cancellationToken);
        Assert.True(extra.IsSuccess);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(new DateOnly(2024, 3, 4), events[^1].FirstTransactionDate);
    }

    [Fact]
    public async Task Delete_PublishesFirstTransactionDate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();

        var portfolioResult = await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken);
        var assetResult = await scope.ServiceProvider.GetRequiredService<AddAssetHandler>().HandleAsync(
            portfolioResult.Value.Id,
            new AddAssetRequest { AssetClass = AssetClass.Stock, Name = "Shares", Currency = "PLN", ManualValue = 0m, ManualValueDate = new DateOnly(2024, 1, 1) },
            cancellationToken);
        var recordHandler = scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>();
        var earliest = await recordHandler.HandleAsync(
            portfolioResult.Value.Id, assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = new DateOnly(2024, 3, 4) },
            cancellationToken);
        await recordHandler.HandleAsync(
            portfolioResult.Value.Id, assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = new DateOnly(2025, 1, 10) },
            cancellationToken);
        var extra = await recordHandler.HandleAsync(
            portfolioResult.Value.Id, assetResult.Value.Id,
            new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 10m, Date = new DateOnly(2025, 2, 1) },
            cancellationToken);

        // Fresh scopes, as in real requests: the remaining rows come from the stored-transaction query, not the change tracker.
        await using var deleteScope = Provider.CreateAsyncScope();
        var deleteResult = await deleteScope.ServiceProvider.GetRequiredService<DeleteTransactionHandler>()
            .HandleAsync(portfolioResult.Value.Id, assetResult.Value.Id, extra.Value.Id, cancellationToken);
        Assert.True(deleteResult.IsSuccess);

        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(new DateOnly(2024, 3, 4), events[^1].FirstTransactionDate);

        await using var deleteEarliestScope = Provider.CreateAsyncScope();
        var deleteEarliest = await deleteEarliestScope.ServiceProvider.GetRequiredService<DeleteTransactionHandler>()
            .HandleAsync(portfolioResult.Value.Id, assetResult.Value.Id, earliest.Value.Id, cancellationToken);
        Assert.True(deleteEarliest.IsSuccess);

        var afterEarliest = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(new DateOnly(2025, 1, 10), afterEarliest[^1].FirstTransactionDate);
    }

    [Fact]
    public async Task Record_PublishesQuantityHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken)).Value.Id;
            (assetId, _) = await AddStockWithTransactionsAsync(arrange.ServiceProvider, portfolioId, "Shares",
            [
                (TransactionType.Buy, 10m, new DateOnly(2024, 3, 4)),
                (TransactionType.Buy, 5m, new DateOnly(2024, 3, 4)),
                (TransactionType.Sell, 3m, new DateOnly(2025, 1, 10))
            ], cancellationToken);
        }

        await using (var record = Provider.CreateAsyncScope())
        {
            var sell = await record.ServiceProvider.GetRequiredService<RecordTransactionHandler>().HandleAsync(
                portfolioId, assetId,
                new RecordTransactionRequest { Type = TransactionType.Sell, Quantity = 2m, UnitPrice = 10m, Date = new DateOnly(2024, 6, 1) },
                cancellationToken);
            Assert.True(sell.IsSuccess, sell.IsFailure ? sell.Error.Code : null);
        }

        await using var verify = Provider.CreateAsyncScope();
        var events = await verify.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var last = events[^1];
        Assert.Equal(10m, last.Quantity);
        Assert.Equal(
            new (DateOnly Date, decimal Quantity)[] { (new DateOnly(2024, 3, 4), 15m), (new DateOnly(2024, 6, 1), 13m), (new DateOnly(2025, 1, 10), 10m) },
            last.Points());
        Assert.Equal(last.FirstTransactionDate, last.Points()[0].Date);
    }

    [Fact]
    public async Task UpdateDate_RewritesQuantityHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        IReadOnlyList<Guid> transactionIds;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken)).Value.Id;
            (assetId, transactionIds) = await AddStockWithTransactionsAsync(arrange.ServiceProvider, portfolioId, "Shares",
            [
                (TransactionType.Buy, 10m, new DateOnly(2024, 3, 4)),
                (TransactionType.Buy, 5m, new DateOnly(2024, 3, 4)),
                (TransactionType.Sell, 3m, new DateOnly(2025, 1, 10)),
                (TransactionType.Sell, 2m, new DateOnly(2024, 6, 1))
            ], cancellationToken);
        }

        await using (var move = Provider.CreateAsyncScope())
        {
            var updateResult = await move.ServiceProvider.GetRequiredService<UpdateTransactionHandler>().HandleAsync(
                portfolioId, assetId, transactionIds[3],
                new UpdateTransactionRequest { Type = TransactionType.Sell, Quantity = 2m, UnitPrice = 10m, Date = new DateOnly(2025, 2, 1) },
                cancellationToken);
            Assert.True(updateResult.IsSuccess, updateResult.IsFailure ? updateResult.Error.Code : null);
        }

        await using var verify = Provider.CreateAsyncScope();
        var events = await verify.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var moved = events[^1];
        Assert.Equal(10m, moved.Quantity);
        Assert.Equal(
            new (DateOnly Date, decimal Quantity)[] { (new DateOnly(2024, 3, 4), 15m), (new DateOnly(2025, 1, 10), 12m), (new DateOnly(2025, 2, 1), 10m) },
            moved.Points());
    }

    [Fact]
    public async Task Delete_RewritesQuantityHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid portfolioId;
        Guid assetId;
        IReadOnlyList<Guid> transactionIds;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Outbox test portfolio" }, cancellationToken)).Value.Id;
            (assetId, transactionIds) = await AddStockWithTransactionsAsync(arrange.ServiceProvider, portfolioId, "Shares",
            [
                (TransactionType.Buy, 10m, new DateOnly(2024, 3, 4)),
                (TransactionType.Buy, 5m, new DateOnly(2024, 3, 4)),
                (TransactionType.Sell, 3m, new DateOnly(2025, 1, 10)),
                (TransactionType.Sell, 2m, new DateOnly(2024, 6, 1))
            ], cancellationToken);
        }

        await using (var move = Provider.CreateAsyncScope())
        {
            var updateResult = await move.ServiceProvider.GetRequiredService<UpdateTransactionHandler>().HandleAsync(
                portfolioId, assetId, transactionIds[3],
                new UpdateTransactionRequest { Type = TransactionType.Sell, Quantity = 2m, UnitPrice = 10m, Date = new DateOnly(2025, 2, 1) },
                cancellationToken);
            Assert.True(updateResult.IsSuccess, updateResult.IsFailure ? updateResult.Error.Code : null);
        }

        await using (var delete = Provider.CreateAsyncScope())
        {
            var deleteResult = await delete.ServiceProvider.GetRequiredService<DeleteTransactionHandler>()
                .HandleAsync(portfolioId, assetId, transactionIds[3], cancellationToken);
            Assert.True(deleteResult.IsSuccess, deleteResult.IsFailure ? deleteResult.Error.Code : null);
        }

        await using var verify = Provider.CreateAsyncScope();
        var events = await verify.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var deleted = events[^1];
        Assert.Equal(12m, deleted.Quantity);
        Assert.Equal(
            new (DateOnly Date, decimal Quantity)[] { (new DateOnly(2024, 3, 4), 15m), (new DateOnly(2025, 1, 10), 12m) },
            deleted.Points());
    }
}
