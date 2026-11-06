using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds.AddBond;
using Skarbiec.Portfolio.Features.Bonds.RedeemBondEarly;
using Skarbiec.Portfolio.Features.Bonds.SettleBondInterest;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

public sealed class PositionEventPublisherTests(SkarbiecContainersFixture containers) : PortfolioOutboxTestBase(containers)
{
    [Fact]
    public async Task Bond_PublishesTerms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Bond terms account", cancellationToken, AccountType.Bonds);
        await FundAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken, 10_000m);
        var request = PortfolioApi.NewBondRequest() with { AccountId = accountId };

        var result = await scope.ServiceProvider.GetRequiredService<AddBondHandler>().HandleAsync(request, cancellationToken);

        Assert.True(result.IsSuccess);
        var evt = (await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Single(e => e.AssetId == result.Value.AssetId);
        var terms = evt.BondTerms;
        Assert.NotNull(terms);
        Assert.Equal(request.SeriesCode, terms.SeriesCode);
        Assert.Equal(request.Type, terms.Type);
        Assert.Equal(request.PurchaseDate, terms.PurchaseDate);
        Assert.Equal(request.PurchasePricePerBond, terms.PurchasePricePerBond);
        Assert.Equal(request.FirstPeriodRatePercent, terms.FirstPeriodRatePercent);
        Assert.Equal(request.MarginPercent, terms.MarginPercent);
        Assert.Equal(request.BondCount, terms.BondCountHistory[request.PurchaseDate]);
        Assert.Empty(terms.SettledPeriodRates);
        Assert.False(terms.IsRedeemed);
    }

    [Fact]
    public async Task Bond_Settled_PublishesSettledPeriodRates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Settled bond account", cancellationToken, AccountType.Bonds);
        await FundAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken, 6_000m);
        var request = PortfolioApi.NewBondRequest(purchaseDate: new DateOnly(2025, 1, 10)) with { AccountId = accountId };
        var added = await scope.ServiceProvider.GetRequiredService<AddBondHandler>().HandleAsync(request, cancellationToken);
        Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
        var assetId = added.Value.AssetId;

        var settled = await scope.ServiceProvider.GetRequiredService<SettleBondInterestHandler>().HandleAsync(
            assetId,
            new SettleBondInterestRequest { Periods = [new SettleBondPeriodRequest { PeriodIndex = 1 }] },
            cancellationToken);

        Assert.True(settled.IsSuccess, settled.IsFailure ? settled.Error.Code : null);
        var evt = (await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Last(e => e.AssetId == assetId);
        var terms = evt.BondTerms;
        Assert.NotNull(terms);
        var rate = Assert.Single(terms.SettledPeriodRates);
        Assert.Equal(1, rate.Key);
        Assert.Equal(request.FirstPeriodRatePercent, rate.Value);
        Assert.False(terms.IsRedeemed);
    }

    [Fact]
    public async Task Bond_EarlyRedeemed_PublishesCountHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Early bond account", cancellationToken, AccountType.Bonds);
        await FundAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken, 6_000m);
        var purchaseDate = new DateOnly(2026, 2, 1);
        var redemptionDate = new DateOnly(2026, 4, 15);
        var added = await scope.ServiceProvider.GetRequiredService<AddBondHandler>().HandleAsync(
            PortfolioApi.NewBondRequest(purchaseDate: purchaseDate, bondCount: 10) with { AccountId = accountId }, cancellationToken);
        Assert.True(added.IsSuccess, added.IsFailure ? added.Error.Code : null);
        var assetId = added.Value.AssetId;

        var redeemed = await scope.ServiceProvider.GetRequiredService<RedeemBondEarlyHandler>().HandleAsync(
            assetId, new RedeemBondEarlyRequest { Date = redemptionDate, BondCount = 4 }, cancellationToken);

        Assert.True(redeemed.IsSuccess, redeemed.IsFailure ? redeemed.Error.Code : null);
        var evt = (await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken)).Last(e => e.AssetId == assetId);
        var terms = evt.BondTerms;
        Assert.NotNull(terms);
        Assert.Equal(2, terms.BondCountHistory.Count);
        Assert.Equal(10, terms.BondCountHistory[purchaseDate]);
        Assert.Equal(6, terms.BondCountHistory[redemptionDate]);
        Assert.False(terms.IsRedeemed);
    }

    [Fact]
    public async Task NonBond_PublishesNoTerms()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var scope = Provider.CreateAsyncScope();
        var accountId = await CreateAccountAsync(scope.ServiceProvider, "Brokerage no terms", cancellationToken);
        await FundAccountCashAsync(scope.ServiceProvider, accountId, cancellationToken, 1_000m);

        var events = await scope.ServiceProvider.GetRequiredService<PortfolioDbContext>()
            .ReadPublishedAsync<AssetPositionChanged>(cancellationToken);

        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Null(e.BondTerms));
    }
}
