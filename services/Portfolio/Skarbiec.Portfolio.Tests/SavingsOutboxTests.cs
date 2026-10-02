using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.SavingsAccounts.AddSavingsAccount;
using Skarbiec.Portfolio.Features.SavingsAccounts.SettleSavingsInterest;
using Skarbiec.Portfolio.Features.SavingsAccounts.UndoSavingsInterestSettlement;
using Skarbiec.Portfolio.Features.SavingsAccounts.UpdateSavingsAccount;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// Outbox tests for savings accounts: add, update, and monthly interest settlement and its undo.
/// The hostless provider and shared arrange helpers live in <see cref="PortfolioOutboxTestBase"/>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class SavingsOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    /// <summary>
    /// savings-accounts AC-2 (outbox half): AddSavingsAccount writes the Savings-class asset, its
    /// ordinary opening Deposit, its <see cref="SavingsAccount"/> terms and one
    /// <see cref="AssetPositionChanged"/> carrying the opening amount as the quantity — all in a single save.
    /// </summary>
    [Fact]
    public async Task AddSavingsAccount_PublishesPositionChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var portfolioId = (await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Savings outbox portfolio" }, cancellationToken)).Value.Id;

        SaveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<AddSavingsAccountHandler>()
            .HandleAsync(portfolioId, PortfolioApi.NewSavingsAccountRequest(), cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);
        var assetId = result.Value.AssetId;

        await using var verify = Provider.CreateAsyncScope();
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
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleSavingsInterestHandler>().HandleAsync(
                portfolioId,
                assetId,
                new SettleSavingsInterestRequest { PeriodEnd = new DateOnly(2026, 1, 31), GrossInterest = 50.00m, Tax = 9.50m },
                cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
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
        await using (var settle = Provider.CreateAsyncScope())
        {
            var settled = await settle.ServiceProvider.GetRequiredService<SettleSavingsInterestHandler>().HandleAsync(
                portfolioId,
                assetId,
                new SettleSavingsInterestRequest { PeriodEnd = new DateOnly(2026, 1, 31), GrossInterest = 50.00m, Tax = 9.50m },
                cancellationToken);
            Assert.True(settled.IsSuccess);
        }

        Guid settlementId;
        await using (var read = Provider.CreateAsyncScope())
        {
            settlementId = (await read.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .Set<SavingsInterestSettlement>().SingleAsync(s => s.AssetId == assetId, cancellationToken)).Id;
        }

        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<UndoSavingsInterestSettlementHandler>()
                .HandleAsync(portfolioId, assetId, settlementId, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
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
        await using var arrange = Provider.CreateAsyncScope();
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

        await using var scope = Provider.CreateAsyncScope();
        var portfolioId = (await scope.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
            .HandleAsync(new CreatePortfolioRequest { Name = "Empty savings outbox portfolio" }, cancellationToken)).Value.Id;

        var result = await scope.ServiceProvider.GetRequiredService<AddSavingsAccountHandler>()
            .HandleAsync(portfolioId, PortfolioApi.NewSavingsAccountRequest(withOpeningDeposit: false), cancellationToken);

        Assert.True(result.IsSuccess);
        await using var verify = Provider.CreateAsyncScope();
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
        await using (var arrange = Provider.CreateAsyncScope())
        {
            portfolioId = (await arrange.ServiceProvider.GetRequiredService<CreatePortfolioHandler>()
                .HandleAsync(new CreatePortfolioRequest { Name = "Update savings outbox portfolio" }, cancellationToken)).Value.Id;
            var added = await arrange.ServiceProvider.GetRequiredService<AddSavingsAccountHandler>()
                .HandleAsync(portfolioId, PortfolioApi.NewSavingsAccountRequest(), cancellationToken);
            Assert.True(added.IsSuccess);
            assetId = added.Value.AssetId;
        }

        int outboxRowsBefore;
        await using (var before = Provider.CreateAsyncScope())
        {
            outboxRowsBefore = await before.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .Set<OutboxMessage>().CountAsync(cancellationToken);
        }

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<UpdateSavingsAccountHandler>()
                .HandleAsync(portfolioId, assetId, PortfolioApi.NewUpdateSavingsAccountRequest(), cancellationToken);
            Assert.True(result.IsSuccess);
        }

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        Assert.Equal(outboxRowsBefore, await verifyDb.Set<OutboxMessage>().CountAsync(cancellationToken));
        var terms = await verifyDb.Set<SavingsAccount>().SingleAsync(t => t.AssetId == assetId, cancellationToken);
        Assert.Equal(3m, terms.AnnualInterestRatePercent);
    }
}
