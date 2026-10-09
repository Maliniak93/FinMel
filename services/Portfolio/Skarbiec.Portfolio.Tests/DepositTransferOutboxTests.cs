using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.Deposits.AddDeposit;
using Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;
using Skarbiec.Portfolio.Features.Deposits.SettleDeposit;
using Skarbiec.Portfolio.Features.Deposits.UpdateDeposit;
using Skarbiec.Portfolio.Features.RemoveAsset;
using Skarbiec.Portfolio.Features.SavingsAccounts.AddSavingsAccount;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class DepositTransferOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task AddFundedDeposit_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid walletId;
        Guid cashId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            walletId = await CreatePortfolioAsync(arrange.ServiceProvider, "Wallet", cancellationToken);
            cashId = await AddCashWithBalanceAsync(arrange.ServiceProvider, walletId, 5_000m, cancellationToken);
            savingsId = await CreatePortfolioAsync(arrange.ServiceProvider, "Savings", cancellationToken);
        }

        int positionEventsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            positionEventsBefore = (await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count;
        }

        SaveChanges.Reset();

        Guid depositId;
        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<AddDepositHandler>().HandleAsync(
                savingsId, PortfolioApi.NewDepositRequest(principal: 1_000m, fundingAssetId: cashId), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
            depositId = result.Value.AssetId;
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task UpdateFundedDeposit_PublishesPositionChangedForBothAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid cashId;
        Guid depositId;
        await using (var arrange = Provider.CreateAsyncScope())
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
                .HandleAsync(savingsId, depositId, request, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task RemoveFundedDeposit_DetachesCashLegWithoutPublishingForIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid cashId;
        Guid depositId;
        await using (var arrange = Provider.CreateAsyncScope())
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
        await using (var before = Provider.CreateAsyncScope())
        {
            positionEventsBefore = (await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count;
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<RemoveAssetHandler>()
                .HandleAsync(savingsId, depositId, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(depositId, Assert.Single(await verifyDb.ReadPublishedAsync<AssetRemoved>(cancellationToken)).AssetId);
        Assert.Equal(positionEventsBefore, (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Count);
        var cashLeg = await verifyDb.Transactions.SingleAsync(t => t.AssetId == cashId && t.Type == TransactionType.Withdraw, cancellationToken);
        Assert.Null(cashLeg.TransferId);
        Assert.Equal(4_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == cashId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task SettleDepositWithDestination_PublishesBothPositions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid walletId;
        Guid cashId;
        Guid depositId;
        await using (var arrange = Provider.CreateAsyncScope())
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
        await using (var before = Provider.CreateAsyncScope())
        {
            eventsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(savingsId, depositId, PortfolioApi.NewSettleRequest(destinationAssetId: cashId), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task SettleDepositIntoSavings_PublishesBothPositions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid depositPortfolioId;
        Guid depositId;
        Guid savingsPortfolioId;
        Guid savingsId;
        await using (var arrange = Provider.CreateAsyncScope())
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
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(depositPortfolioId, depositId, PortfolioApi.NewSettleRequest(destinationAssetId: savingsId), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
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

    [Fact]
    public async Task SettleDepositWithInvalidDestination_WritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid stockId;
        Guid depositId;
        await using (var arrange = Provider.CreateAsyncScope())
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
        await using (var before = Provider.CreateAsyncScope())
        {
            var beforeDb = before.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            outboxRowsBefore = await beforeDb.Set<OutboxMessage>().CountAsync(cancellationToken);
            transactionsBefore = await beforeDb.Transactions.CountAsync(cancellationToken);
        }

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleDepositHandler>()
                .HandleAsync(savingsId, depositId, PortfolioApi.NewSettleRequest(destinationAssetId: stockId), cancellationToken);
            Assert.True(result.IsFailure);
            Assert.Equal(PortfolioAssertions.InvalidTransferCounterpartErrorCode, result.Error.Code);
        }

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(transactionsBefore, await verifyDb.Transactions.CountAsync(cancellationToken));
        Assert.Null((await verifyDb.Set<TermDeposit>().SingleAsync(t => t.AssetId == depositId, cancellationToken)).SettledOn);
        Assert.Equal(10_000m, (await verifyDb.Assets.SingleAsync(a => a.Id == depositId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task PayOutDeposit_PublishesBothPositions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid savingsId;
        Guid walletId;
        Guid cashId;
        Guid depositId;
        await using (var arrange = Provider.CreateAsyncScope())
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
        await using (var before = Provider.CreateAsyncScope())
        {
            eventsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<PayOutDepositHandler>()
                .HandleAsync(savingsId, depositId, PortfolioApi.NewPayOutRequest(cashId), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        int outboxRowsAfterPayOut;
        await using (var verify = Provider.CreateAsyncScope())
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

        await using (var again = Provider.CreateAsyncScope())
        {
            var second = await again.ServiceProvider.GetRequiredService<PayOutDepositHandler>()
                .HandleAsync(savingsId, depositId, PortfolioApi.NewPayOutRequest(cashId), cancellationToken);
            Assert.True(second.IsFailure);
            Assert.Equal(PortfolioAssertions.DepositAlreadyPaidOutErrorCode, second.Error.Code);
        }

        await using var final = Provider.CreateAsyncScope();
        var finalDb = final.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsAfterPayOut, await finalDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        Assert.Equal(15_119.83m, (await finalDb.Assets.SingleAsync(a => a.Id == cashId, cancellationToken)).Quantity);
    }
}
