using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds.AddBond;
using Skarbiec.Portfolio.Features.Bonds.SettleBondInterest;
using Skarbiec.Portfolio.Features.Bonds.UndoBondInterestSettlement;
using Skarbiec.Portfolio.Features.Bonds.UpdateBond;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class BondOutboxTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task AddBond_PublishesPosition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Bond outbox portfolio", cancellationToken);

        SaveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<AddBondHandler>()
            .HandleAsync(portfolioId, PortfolioApi.NewBondRequest(), cancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, SaveChanges.Count);
        var assetId = result.Value.AssetId;

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var asset = await verifyDb.Assets.SingleAsync(a => a.Id == assetId, cancellationToken);
        Assert.Equal(AssetClass.Bond, asset.AssetClass);
        Assert.Equal(5_000m, asset.Quantity);
        Assert.Equal(1, await verifyDb.Transactions.CountAsync(t => t.AssetId == assetId, cancellationToken));
        Assert.True(await verifyDb.Set<TreasuryBond>().AnyAsync(t => t.AssetId == assetId, cancellationToken));

        var evt = Assert.Single(await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken));
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(portfolioId, evt.PortfolioId);
        Assert.Equal(UserId, evt.UserId);
        Assert.Equal(AssetClass.Bond, evt.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, evt.ValuationMode);
        Assert.Equal("PLN", evt.Currency);
        Assert.Equal(5_000m, evt.Quantity);
        Assert.Null(evt.InstrumentId);
        Assert.Null(evt.ManualValueAmount);
        Assert.False(evt.PortfolioIsArchived);
    }

    [Fact]
    public async Task AddBond_FundedFromCash_PublishesBothPositionsInOneSave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var scope = Provider.CreateAsyncScope();
        var walletId = await CreatePortfolioAsync(scope.ServiceProvider, "Wallet", cancellationToken);
        var cashId = await AddCashWithBalanceAsync(scope.ServiceProvider, walletId, 6_000m, cancellationToken);
        var bondsId = await CreatePortfolioAsync(scope.ServiceProvider, "Bonds", cancellationToken);
        var eventsBefore = await CountPositionEventsAsync(cancellationToken);

        SaveChanges.Reset();

        var result = await scope.ServiceProvider.GetRequiredService<AddBondHandler>()
            .HandleAsync(bondsId, PortfolioApi.NewBondRequest(fundingAssetId: cashId), cancellationToken);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore).ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(1_000m, Assert.Single(events, e => e.AssetId == cashId).Quantity);
        Assert.Equal(5_000m, Assert.Single(events, e => e.AssetId == result.Value.AssetId).Quantity);
    }

    [Fact]
    public async Task UpdateBond_FundedFromCash_PublishesBothPositionsInOneSave()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        Guid bondsId;
        Guid cashId;
        Guid assetId;
        await using (var arrange = Provider.CreateAsyncScope())
        {
            var walletId = await CreatePortfolioAsync(arrange.ServiceProvider, "Wallet", cancellationToken);
            cashId = await AddCashWithBalanceAsync(arrange.ServiceProvider, walletId, 6_000m, cancellationToken);
            bondsId = await CreatePortfolioAsync(arrange.ServiceProvider, "Bonds", cancellationToken);
            var added = await arrange.ServiceProvider.GetRequiredService<AddBondHandler>()
                .HandleAsync(bondsId, PortfolioApi.NewBondRequest(fundingAssetId: cashId), cancellationToken);
            Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
            assetId = added.Value.AssetId;
        }

        var eventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var request = PortfolioApi.NewBondRequest(bondCount: 40).ToUpdateRequest();
            var result = await act.ServiceProvider.GetRequiredService<UpdateBondHandler>()
                .HandleAsync(bondsId, assetId, request, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore).ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(2_000m, Assert.Single(events, e => e.AssetId == cashId).Quantity);
        Assert.Equal(4_000m, Assert.Single(events, e => e.AssetId == assetId).Quantity);
    }

    [Fact]
    public async Task SettleBondInterest_PublishesAtomically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (bondsId, cashId, assetId) = await ArrangeFundedRorAsync(cancellationToken);
        var eventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.FailNext();

        await using (var failing = Provider.CreateAsyncScope())
        {
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => failing.ServiceProvider
                .GetRequiredService<SettleBondInterestHandler>()
                .HandleAsync(bondsId, assetId, NewCouponSettlement(cashId), cancellationToken));
        }

        Assert.Equal(eventsBefore, await CountPositionEventsAsync(cancellationToken));
        await using (var check = Provider.CreateAsyncScope())
        {
            var checkDb = check.ServiceProvider.GetRequiredService<PortfolioDbContext>();
            Assert.False(await checkDb.Set<BondInterestSettlement>().AnyAsync(s => s.AssetId == assetId, cancellationToken));
        }

        SaveChanges.Reset();

        await using (var retry = Provider.CreateAsyncScope())
        {
            var result = await retry.ServiceProvider.GetRequiredService<SettleBondInterestHandler>()
                .HandleAsync(bondsId, assetId, NewCouponSettlement(cashId), cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore).ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(5_000m, Assert.Single(events, e => e.AssetId == assetId).Quantity);
        Assert.Equal(1_013.36m, Assert.Single(events, e => e.AssetId == cashId).Quantity);
        var settlement = await verifyDb.Set<BondInterestSettlement>().SingleAsync(s => s.AssetId == assetId, cancellationToken);
        Assert.Equal(16.50m, settlement.GrossInterest);
        Assert.Equal(3.14m, settlement.Tax);
    }

    [Fact]
    public async Task SettleBondInterest_Capitalising_PublishesBondPosition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var portfolioId = await CreatePortfolioAsync(scope.ServiceProvider, "Bonds", cancellationToken);
        var added = await scope.ServiceProvider.GetRequiredService<AddBondHandler>()
            .HandleAsync(portfolioId, PortfolioApi.NewBondRequest(purchaseDate: new DateOnly(2025, 1, 10)), cancellationToken);
        Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
        var assetId = added.Value.AssetId;
        var eventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<SettleBondInterestHandler>().HandleAsync(
                portfolioId,
                assetId,
                new SettleBondInterestRequest { Periods = [new SettleBondPeriodRequest { PeriodIndex = 1 }] },
                cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore).ToList();
        var evt = Assert.Single(events);
        Assert.Equal(assetId, evt.AssetId);
        Assert.Equal(5_267.50m, evt.Quantity);
        Assert.Equal(AssetClass.Bond, evt.AssetClass);
        Assert.Equal(UserId, evt.UserId);
    }

    [Fact]
    public async Task UndoBondInterestSettlement_PublishesBothPositions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (bondsId, cashId, assetId) = await ArrangeFundedRorAsync(cancellationToken);
        await using (var settle = Provider.CreateAsyncScope())
        {
            var settled = await settle.ServiceProvider.GetRequiredService<SettleBondInterestHandler>()
                .HandleAsync(bondsId, assetId, NewCouponSettlement(cashId), cancellationToken);
            Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        }

        Guid settlementId;
        await using (var read = Provider.CreateAsyncScope())
        {
            settlementId = (await read.ServiceProvider.GetRequiredService<PortfolioDbContext>()
                .Set<BondInterestSettlement>().SingleAsync(s => s.AssetId == assetId, cancellationToken)).Id;
        }

        var eventsBefore = await CountPositionEventsAsync(cancellationToken);
        SaveChanges.Reset();

        await using (var act = Provider.CreateAsyncScope())
        {
            var result = await act.ServiceProvider.GetRequiredService<UndoBondInterestSettlementHandler>()
                .HandleAsync(bondsId, assetId, settlementId, cancellationToken);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        Assert.Equal(1, SaveChanges.Count);

        await using var verify = Provider.CreateAsyncScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<PortfolioDbContext>();
        var events = (await verifyDb.ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Skip(eventsBefore).ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(5_000m, Assert.Single(events, e => e.AssetId == assetId).Quantity);
        Assert.Equal(1_000m, Assert.Single(events, e => e.AssetId == cashId).Quantity);
    }

    private static SettleBondInterestRequest NewCouponSettlement(Guid cashId) => new()
    {
        Periods = [new SettleBondPeriodRequest { PeriodIndex = 1 }],
        DestinationAssetId = cashId
    };

    private async Task<(Guid BondsId, Guid CashId, Guid AssetId)> ArrangeFundedRorAsync(CancellationToken cancellationToken)
    {
        await using var arrange = Provider.CreateAsyncScope();
        var walletId = await CreatePortfolioAsync(arrange.ServiceProvider, "Wallet", cancellationToken);
        var cashId = await AddCashWithBalanceAsync(arrange.ServiceProvider, walletId, 6_000m, cancellationToken);
        var bondsId = await CreatePortfolioAsync(arrange.ServiceProvider, "Bonds", cancellationToken);
        var added = await arrange.ServiceProvider.GetRequiredService<AddBondHandler>().HandleAsync(
            bondsId,
            PortfolioApi.NewRorBondRequest(purchaseDate: new DateOnly(2026, 1, 10)) with { FundingAssetId = cashId },
            cancellationToken);
        Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);

        return (bondsId, cashId, added.Value.AssetId);
    }
}
