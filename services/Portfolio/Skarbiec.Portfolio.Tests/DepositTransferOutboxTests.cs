using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class DepositTransferOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task AddDepositFromBankAccount_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        CreatedBank source;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            source = await CreateBankAccountAsync(arrange.ServiceProvider, "mBank", "PLN", 0m, cancellationToken, balance: 5_000m);
        }

        var positionEventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        Guid depositId;
        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<AddDepositHandler>().HandleAsync(
                PortfolioApi.NewDepositRequest(principal: 1_000m, sourceAccountId: source.Id), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
            depositId = result.Value.AssetId;
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(positionEventsBefore).ToList();
        Assert.Equal(2, events.Count);
        var bankEvent = Assert.Single(events, e => e.AssetId == source.CashAssetId);
        Assert.Equal(4_000m, bankEvent.Quantity);
        Assert.Equal(source.Id, bankEvent.AccountId);
        Assert.Equal(AssetClass.BankAccount, bankEvent.AssetClass);
        Assert.Equal(UserId, bankEvent.UserId);
        var depositEvent = Assert.Single(events, e => e.AssetId == depositId);
        Assert.Equal(1_000m, depositEvent.Quantity);
        Assert.Null(depositEvent.AccountId);
        Assert.Equal(AssetClass.Deposit, depositEvent.AssetClass);

        var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.Equal(4_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == source.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(1_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == depositId, cancellationToken)).Quantity);
        var terms = await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == depositId, cancellationToken);
        Assert.Equal(source.Id, terms.SourceAccountId);
        Assert.Equal(source.Id, terms.ReturnAccountId);
    }

    [Fact]
    public async Task AddDepositExceedingBalance_WritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        CreatedBank source;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            source = await CreateBankAccountAsync(arrange.ServiceProvider, "mBank", "PLN", 0m, cancellationToken, balance: 5_000m);
        }

        int outboxRowsBefore;
        int transactionsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            var beforeDb = before.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            outboxRowsBefore = await beforeDb.Set<OutboxMessage>().CountAsync(cancellationToken);
            transactionsBefore = await beforeDb.Transactions.CountAsync(cancellationToken);
        }

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<AddDepositHandler>().HandleAsync(
                PortfolioApi.NewDepositRequest(principal: 5_000.01m, sourceAccountId: source.Id), cancellationToken);
            Assert.True(result.IsFailure);
            Assert.Equal(PortfolioAssertions.InsufficientFundsErrorCode, result.Error.Code);
        }

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(transactionsBefore, await verifyDb.Transactions.CountAsync(cancellationToken));
        Assert.Equal(0, await verifyDb.Set<TermDeposit>().CountAsync(cancellationToken));
    }

    [Fact]
    public async Task UpdateDeposit_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        CreatedBank source;
        Guid depositId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            source = await CreateBankAccountAsync(arrange.ServiceProvider, "mBank", "PLN", 0m, cancellationToken, balance: 5_000m);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>().HandleAsync(
                PortfolioApi.NewDepositRequest(principal: 1_000m, sourceAccountId: source.Id), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            depositId = added.Value.AssetId;
        }

        IReadOnlyList<AssetPositionChanged> eventsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            eventsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var request = PortfolioApi.NewDepositRequest(principal: 1_500m, startDate: new DateOnly(2026, 2, 1)).ToUpdateRequest();
            var result = await act.ServiceProvider.GetRequiredService<UpdateDepositHandler>()
                .HandleAsync(depositId, request, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore.Count).ToList();
        Assert.Equal(2, events.Count);
        var bankEvent = Assert.Single(events, e => e.AssetId == source.CashAssetId);
        Assert.Equal(3_500m, bankEvent.Quantity);
        Assert.True(bankEvent.Version > eventsBefore.Where(e => e.AssetId == source.CashAssetId).Max(e => e.Version));
        var depositEvent = Assert.Single(events, e => e.AssetId == depositId);
        Assert.Equal(1_500m, depositEvent.Quantity);
        Assert.True(depositEvent.Version > eventsBefore.Where(e => e.AssetId == depositId).Max(e => e.Version));

        var legs = await verifyDb.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.All(legs, leg => Assert.Equal(1_500m, leg.Quantity));
        Assert.All(legs, leg => Assert.Equal(new DateOnly(2026, 2, 1), leg.Date));
    }

    [Fact]
    public async Task RemoveDeposit_DetachesBankLegWithoutPublishingForIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        CreatedBank source;
        Guid depositId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            source = await CreateBankAccountAsync(arrange.ServiceProvider, "mBank", "PLN", 0m, cancellationToken, balance: 5_000m);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>().HandleAsync(
                PortfolioApi.NewDepositRequest(principal: 1_000m, sourceAccountId: source.Id), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            depositId = added.Value.AssetId;
        }

        var positionEventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
                .HandleAsync(depositId, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(depositId, Assert.Single(await verifyDb.ReadPublishedAsync<AssetRemoved>(cancellationToken)).AssetId);
        Assert.Equal(positionEventsBefore, (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count);
        var bankLeg = await verifyDb.Transactions.SingleAsync(
            t => t.AssetId == source.CashAssetId && t.Type == TransactionType.Withdraw, cancellationToken);
        Assert.Null(bankLeg.TransferId);
        Assert.Equal(4_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == source.CashAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task SettleDepositWithPayOut_PublishesBothPositions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        CreatedBank nest;
        Guid depositId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            nest = await CreateBankAccountAsync(arrange.ServiceProvider, "Nest", "PLN", 5m, cancellationToken);
            var source = await CreateBankAccountAsync(arrange.ServiceProvider, "mBank", "PLN", 0m, cancellationToken, balance: 20_000m);
            var added = await arrange.ServiceProvider.GetRequiredService<AddDepositHandler>().HandleAsync(
                PortfolioApi.NewDepositRequest(sourceAccountId: source.Id, returnAccountId: nest.Id), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            depositId = added.Value.AssetId;
        }

        IReadOnlyList<AssetPositionChanged> eventsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            eventsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(depositId, PortfolioApi.NewSettleRequest(payOut: true), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore.Count).ToList();
        Assert.Equal(2, events.Count);
        var depositEvent = Assert.Single(events, e => e.AssetId == depositId);
        Assert.Equal(0m, depositEvent.Quantity);
        Assert.Equal(AssetClass.Deposit, depositEvent.AssetClass);
        Assert.Equal(UserId, depositEvent.UserId);
        Assert.True(depositEvent.Version > eventsBefore.Where(e => e.AssetId == depositId).Max(e => e.Version));
        var nestEvent = Assert.Single(events, e => e.AssetId == nest.CashAssetId);
        Assert.Equal(10_119.83m, nestEvent.Quantity);
        Assert.Equal(nest.Id, nestEvent.AccountId);
        Assert.Equal(AssetClass.BankAccount, nestEvent.AssetClass);

        Assert.Equal(0m, (await verifyDb.Assets.SingleAsync(a => a.Id == depositId, cancellationToken)).Quantity);
        Assert.Equal(10_119.83m, (await verifyDb.Assets.SingleAsync(a => a.Id == nest.CashAssetId, cancellationToken)).Quantity);
        var payoutLegs = await verifyDb.Transactions
            .Where(t => t.TransferId != null && t.Date == new DateOnly(2026, 4, 15))
            .ToListAsync(cancellationToken);
        Assert.Equal(2, payoutLegs.Count);
        Assert.Single(payoutLegs.Select(t => t.TransferId).Distinct());
        Assert.All(payoutLegs, leg => Assert.Equal(10_119.83m, leg.Quantity));
        var terms = await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == depositId, cancellationToken);
        Assert.Equal(new DateOnly(2026, 4, 15), terms.SettledOn);
    }

    [Fact]
    public async Task PayOutDeposit_PublishesBothPositions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        OpenedDeposit opened;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            opened = await OpenDepositAsync(arrange.ServiceProvider, cancellationToken);
            var settled = await arrange.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(opened.AssetId, PortfolioApi.NewSettleRequest(), cancellationToken);
            Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        }

        IReadOnlyList<AssetPositionChanged> eventsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            eventsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<PayOutDepositHandler>()
                .HandleAsync(opened.AssetId, PortfolioApi.NewPayOutRequest(), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        int outboxRowsAfterPayOut;
        await using (var verify = Provider.CreateAsyncScope())
        {
            var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore.Count).ToList();
            Assert.Equal(2, events.Count);
            var depositEvent = Assert.Single(events, e => e.AssetId == opened.AssetId);
            Assert.Equal(0m, depositEvent.Quantity);
            Assert.Equal(AssetClass.Deposit, depositEvent.AssetClass);
            Assert.True(depositEvent.Version > eventsBefore.Where(e => e.AssetId == opened.AssetId).Max(e => e.Version));
            var bankEvent = Assert.Single(events, e => e.AssetId == opened.SourceMoneyAssetId);
            Assert.Equal(1_000_000m - 10_000m + 10_119.83m, bankEvent.Quantity);
            Assert.Equal(opened.SourceAccountId, bankEvent.AccountId);
            Assert.True(bankEvent.Version > eventsBefore.Where(e => e.AssetId == opened.SourceMoneyAssetId).Max(e => e.Version));

            var payoutLegs = await verifyDb.Transactions
                .Where(t => t.TransferId != null && t.Date == new DateOnly(2026, 4, 18))
                .ToListAsync(cancellationToken);
            Assert.Equal(2, payoutLegs.Count);
            Assert.All(payoutLegs, leg => Assert.Equal(10_119.83m, leg.Quantity));
            outboxRowsAfterPayOut = await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        await using (var again = Provider.CreateAsyncScope())
        {
            var second = await again.ServiceProvider.GetRequiredService<PayOutDepositHandler>()
                .HandleAsync(opened.AssetId, PortfolioApi.NewPayOutRequest(), cancellationToken);
            Assert.True(second.IsFailure);
            Assert.Equal(PortfolioAssertions.DepositAlreadyPaidOutErrorCode, second.Error.Code);
        }

        await using var final = Provider.CreateAsyncScope();
        var finalDb = final.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsAfterPayOut, await finalDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(
            1_000_000m - 10_000m + 10_119.83m,
            (await finalDb.Assets.SingleAsync(a => a.Id == opened.SourceMoneyAssetId, cancellationToken)).Quantity);
    }
}
