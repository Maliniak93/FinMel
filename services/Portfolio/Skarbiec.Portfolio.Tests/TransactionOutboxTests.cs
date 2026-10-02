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
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// Outbox tests for transaction writes: record, update and delete each publish the recomputed quantity.
/// The hostless provider and shared arrange helpers live in <see cref="PortfolioOutboxTestBase"/>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class TransactionOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    /// <summary>spec-02 AC-3: published Quantity equals the recomputed quantity.</summary>
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

    /// <summary>spec-02 AC-4: editing a transaction now publishes — nothing was published before.</summary>
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

    /// <summary>spec-02 AC-5: deleting a transaction now publishes — nothing was published before.</summary>
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

        // The last event in write order is the delete's — matching on the quantity alone would be
        // ambiguous here, since the Buy that preceded the Sell also left the position at 10.
        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        var deleteEvent = events[^1];
        Assert.Equal(10m, deleteEvent.Quantity);
        Assert.Equal(assetResult.Value.Id, deleteEvent.AssetId);
    }
}
