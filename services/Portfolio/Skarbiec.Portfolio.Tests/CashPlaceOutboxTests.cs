using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.CashPlaces.CreateCashPlace;
using Skarbiec.Portfolio.Features.CashPlaces.DeleteCashPlace;
using Skarbiec.Portfolio.Features.CashPlaces.UpdateCashPlace;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class CashPlaceOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    private static readonly DateOnly Day1 = new(2026, 10, 1);

    [Fact]
    public async Task CreateCashPlace_WritesAPositionPerBalanceInSameSave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        SaveChanges.Reset();

        var created = await scope.ServiceProvider.GetRequiredService<CreateCashPlaceHandler>().HandleAsync(
            new CreateCashPlaceRequest { Name = "Sejf", Currencies = ["PLN", "USD"] }, cancellationToken);

        Assert.True(created.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);
        var balances = await dbContext.Assets.Where(a => a.CashPlaceId == created.Value.Id).ToListAsync(cancellationToken);
        Assert.Equal(["PLN", "USD"], balances.Select(a => a.Currency).Order().ToArray());
        var events = await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        Assert.Equal(2, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal(AssetClass.Cash, e.AssetClass);
            Assert.Equal(AssetValuationMode.CurrencyValued, e.ValuationMode);
            Assert.Null(e.AccountId);
            Assert.Equal(0m, e.Quantity);
        });
        Assert.Equal(
            balances.Select(a => (a.Id, a.Currency)).Order().ToArray(),
            events.Select(e => (e.AssetId, e.Currency)).Order().ToArray());
    }

    [Fact]
    public async Task UsdDeposit_PublishesCashPositionWithoutAccountInSameSave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var created = await scope.ServiceProvider.GetRequiredService<CreateCashPlaceHandler>().HandleAsync(
            new CreateCashPlaceRequest { Name = "Sejf", Currencies = ["PLN", "USD"] }, cancellationToken);
        var usdId = await dbContext.Assets
            .Where(a => a.CashPlaceId == created.Value.Id && a.Currency == "USD")
            .Select(a => a.Id)
            .SingleAsync(cancellationToken);
        SaveChanges.Reset();

        var deposit = await scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            usdId, new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = 300m, UnitPrice = 1m, Date = Day1 }, cancellationToken);

        Assert.True(deposit.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);
        var evt = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Last(e => e.AssetId == usdId);
        Assert.Equal(AssetClass.Cash, evt.AssetClass);
        Assert.Equal("USD", evt.Currency);
        Assert.Null(evt.AccountId);
        Assert.Equal(300m, evt.Quantity);
        Assert.Equal([(Day1, 300m)], evt.Points());
    }

    [Fact]
    public async Task RenameCashPlace_RenamesBalancesAndPublishesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var created = await scope.ServiceProvider.GetRequiredService<CreateCashPlaceHandler>().HandleAsync(
            new CreateCashPlaceRequest { Name = "Sejf", Currencies = ["PLN", "USD"] }, cancellationToken);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var renamed = await scope.ServiceProvider.GetRequiredService<UpdateCashPlaceHandler>().HandleAsync(
            created.Value.Id, new UpdateCashPlaceRequest { Name = "Skrytka" }, cancellationToken);

        Assert.True(renamed.IsSuccess);
        Assert.Equal(outboxRowsBefore, await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken));
        var names = await dbContext.Assets.Where(a => a.CashPlaceId == created.Value.Id).Select(a => a.Name).ToListAsync(cancellationToken);
        Assert.Equal(["Skrytka", "Skrytka"], names);
    }

    [Fact]
    public async Task DeleteCashPlace_WritesAssetRemovedPerBalanceInSameSave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var created = await scope.ServiceProvider.GetRequiredService<CreateCashPlaceHandler>().HandleAsync(
            new CreateCashPlaceRequest { Name = "Sejf", Currencies = ["PLN", "USD"] }, cancellationToken);
        var balanceIds = await dbContext.Assets
            .Where(a => a.CashPlaceId == created.Value.Id)
            .Select(a => a.Id)
            .ToListAsync(cancellationToken);
        SaveChanges.Reset();

        var deleted = await scope.ServiceProvider.GetRequiredService<DeleteCashPlaceHandler>().HandleAsync(created.Value.Id, cancellationToken);

        Assert.True(deleted.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);
        var removed = await dbContext.ReadPublishedAsync<AssetRemoved>(cancellationToken);
        Assert.Equal(balanceIds.Order().ToArray(), removed.Select(r => r.AssetId).Order().ToArray());
        Assert.All(removed, r => Assert.Null(r.AccountId));
        Assert.False(await dbContext.Assets.AnyAsync(a => a.CashPlaceId == created.Value.Id, cancellationToken));
    }
}
