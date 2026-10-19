using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts.BankInterest.SettleBankInterest;
using Skarbiec.Portfolio.Features.Accounts.BankInterest.UndoBankInterestSettlement;
using Skarbiec.Portfolio.Features.Accounts.UpdateAccount;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class BankAccountOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    private static readonly DateOnly OpeningDay = new(2026, 1, 1);

    [Fact]
    public async Task BalanceGroupChange_PublishesBalanceWithNextVersion()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var groupId = await CreateGroupAsync(scope.ServiceProvider, "Poduszka", cancellationToken);
        var otherId = await CreateGroupAsync(scope.ServiceProvider, "Akcje", cancellationToken);
        var account = await CreateBankAccountAsync(scope.ServiceProvider, "mBank", "PLN", 0m, cancellationToken, groupId: groupId);
        var before = await dbContext.Assets.AsNoTracking().SingleAsync(a => a.Id == account.CashAssetId, cancellationToken);

        var updated = await scope.ServiceProvider.GetRequiredService<UpdateAccountHandler>().HandleAsync(
            account.Id,
            new UpdateAccountRequest { Name = "mBank", Variant = BankVariant.Savings, BankName = "mBank", AnnualInterestRatePercent = 0m, GroupId = otherId },
            cancellationToken);

        Assert.True(updated.IsSuccess);
        var evt = Assert.Single(
            await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken),
            e => e.AssetId == account.CashAssetId && e.Version == before.Version + 1);
        Assert.Equal(otherId, evt.GroupId);
    }

    [Fact]
    public async Task Deposit_PublishesBankAccountPositionWithAccountAndCurrency()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var account = await CreateBankAccountAsync(scope.ServiceProvider, "mBank USD", "USD", 0m, cancellationToken);
        SaveChanges.Reset();

        var deposit = await scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            account.CashAssetId,
            new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = 100m, UnitPrice = 1m, Date = OpeningDay },
            cancellationToken);

        Assert.True(deposit.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);
        var events = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken))
            .Where(e => e.AssetId == account.CashAssetId).ToList();
        Assert.Equal(2, events.Count);
        var last = events[^1];
        Assert.Equal(AssetClass.BankAccount, last.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, last.ValuationMode);
        Assert.Equal("USD", last.Currency);
        Assert.Equal(account.Id, last.AccountId);
        Assert.Equal(100m, last.Quantity);
    }

    [Fact]
    public async Task SettleInterest_PublishesBankAccountPositionWithCreditedNet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var account = await CreateBankAccountAsync(scope.ServiceProvider, "Nest Oszczędnościowe", "PLN", 5m, cancellationToken);
        var recorded = await scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            account.CashAssetId,
            new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = 10_000m, UnitPrice = 1m, Date = OpeningDay },
            cancellationToken);
        Assert.True(recorded.IsSuccess);
        SaveChanges.Reset();

        var settled = await scope.ServiceProvider.GetRequiredService<SettleBankInterestHandler>().HandleAsync(
            account.Id,
            new SettleBankInterestRequest { PeriodEnd = new DateOnly(2026, 1, 31), GrossInterest = 50.00m, Tax = 9.50m },
            cancellationToken);

        Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        var last = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken))
            .Where(e => e.AssetId == account.CashAssetId).Last();
        Assert.Equal(AssetClass.BankAccount, last.AssetClass);
        Assert.Equal("PLN", last.Currency);
        Assert.Equal(account.Id, last.AccountId);
        Assert.Equal(UserId, last.UserId);
        Assert.Equal(10_040.50m, last.Quantity);
        Assert.Equal(10_040.50m, (await dbContext.Assets.SingleAsync(a => a.Id == account.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task UndoInterestSettlement_PublishesBankAccountPositionWithRestoredBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var account = await CreateBankAccountAsync(scope.ServiceProvider, "Nest Oszczędnościowe", "PLN", 5m, cancellationToken);
        var recorded = await scope.ServiceProvider.GetRequiredService<RecordTransactionHandler>().HandleAsync(
            account.CashAssetId,
            new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = 10_000m, UnitPrice = 1m, Date = OpeningDay },
            cancellationToken);
        Assert.True(recorded.IsSuccess);
        var settled = await scope.ServiceProvider.GetRequiredService<SettleBankInterestHandler>().HandleAsync(
            account.Id,
            new SettleBankInterestRequest { PeriodEnd = new DateOnly(2026, 1, 31), GrossInterest = 50.00m, Tax = 9.50m },
            cancellationToken);
        Assert.True(settled.IsSuccess);
        SaveChanges.Reset();

        var undone = await scope.ServiceProvider.GetRequiredService<UndoBankInterestSettlementHandler>().HandleAsync(
            account.Id, settled.Value.SettlementId, cancellationToken);

        Assert.True(undone.IsSuccess, undone.IsFailure ? undone.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);
        var events = (await dbContext.ReadPublishedAsync<AssetPositionChanged>(cancellationToken))
            .Where(e => e.AssetId == account.CashAssetId).ToList();
        var last = events[^1];
        Assert.Equal(AssetClass.BankAccount, last.AssetClass);
        Assert.Equal(account.Id, last.AccountId);
        Assert.Equal(10_000m, last.Quantity);
        Assert.True(last.Version > events[^2].Version);
        Assert.False(await dbContext.BankInterestSettlements.AnyAsync(s => s.AssetId == account.CashAssetId, cancellationToken));
    }
}
