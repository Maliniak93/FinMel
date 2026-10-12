using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts.CreateAccount;
using Skarbiec.Portfolio.Features.Accounts.DeleteAccount;
using Skarbiec.Portfolio.Features.Accounts.UpdateAccount;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class AccountCashOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    private static readonly DateOnly Day1 = new(2026, 10, 1);

    private static readonly DateOnly Day2 = new(2026, 10, 2);

    [Fact]
    public async Task CreateAccount_WritesCashPositionInSameSaveAsAccountAndCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        SaveChanges.Reset();

        var created = await scope.ServiceProvider.GetRequiredService<CreateAccountHandler>().HandleAsync(
            new CreateAccountRequest { Name = "XTB IKE", Type = AccountType.Brokerage, Wrapper = TaxWrapper.Ike },
            cancellationToken);

        Assert.True(created.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);
        var evt = Assert.Single(await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken));
        Assert.Equal(created.Value.CashAssetId, evt.AssetId);
        Assert.Equal(created.Value.Id, evt.AccountId);
        Assert.Equal(AssetClass.Cash, evt.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, evt.ValuationMode);
        Assert.Equal("PLN", evt.Currency);
        Assert.Equal(0m, evt.Quantity);
    }

    [Fact]
    public async Task DepositAndWithdraw_PublishCashQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "XTB", cancellationToken);
        var cashId = await dbContext.Assets.Where(a => a.AccountId == accountId).Select(a => a.Id).SingleAsync(cancellationToken);
        var record = scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>();

        var deposit = await record.HandleAsync(
            cashId, new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = 5_000m, UnitPrice = 1m, Date = Day1 }, cancellationToken);
        var withdraw = await record.HandleAsync(
            cashId, new RecordTransactionRequest { Type = TransactionType.Withdraw, Quantity = 1_000m, UnitPrice = 1m, Date = Day2 }, cancellationToken);

        Assert.True(deposit.IsSuccess);
        Assert.True(withdraw.IsSuccess);
        var events = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Where(e => e.AssetId == cashId).ToList();
        Assert.Equal(4_000m, events[^1].Quantity);
        Assert.Equal([(Day1, 5_000m), (Day2, 4_000m)], events[^1].Points());
    }

    [Fact]
    public async Task RenameAccount_PublishesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "XTB", cancellationToken);
        var outboxRowsBefore = await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken);

        var renamed = await scope.ServiceProvider.GetRequiredService<UpdateAccountHandler>().HandleAsync(
            accountId, new UpdateAccountRequest { Name = "XTB IKE" }, cancellationToken);

        Assert.True(renamed.IsSuccess);
        Assert.Equal(outboxRowsBefore, await dbContext.Set<OutboxMessage>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task DeleteAccount_WritesAssetRemovedForCashInSameSave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "XTB", cancellationToken);
        var cashId = await dbContext.Assets.Where(a => a.AccountId == accountId).Select(a => a.Id).SingleAsync(cancellationToken);
        SaveChanges.Reset();

        var deleted = await scope.ServiceProvider.GetRequiredService<DeleteAccountHandler>().HandleAsync(accountId, cancellationToken);

        Assert.True(deleted.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);
        var removed = Assert.Single(await dbContext.ReadPublishedAsync<AssetRemoved>(cancellationToken));
        Assert.Equal(cashId, removed.AssetId);
        Assert.Equal(accountId, removed.AccountId);
        Assert.False(await dbContext.Assets.AnyAsync(a => a.AccountId == accountId, cancellationToken));
    }
}
