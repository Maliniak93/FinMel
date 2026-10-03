using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class AddBondEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Add_Valid_CreatesAssetTermsAndOpeningTransaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Bonds");

        var response = await client.PostAsJsonAsync(BondsUri(portfolioId), NewBondRequest(), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(BondUri(portfolioId, body.AssetId), response.Headers.Location?.OriginalString);
        Assert.Equal(portfolioId, body.PortfolioId);
        Assert.Equal("Bonds", body.PortfolioName);
        Assert.Equal("EDO1036", body.SeriesCode);
        Assert.Equal(TreasuryBondType.Edo, body.Type);
        Assert.Equal(new DateOnly(2026, 10, 1), body.PurchaseDate);
        Assert.Equal(50, body.BondCount);
        Assert.Equal(100.00m, body.PurchasePricePerBond);
        Assert.Equal(5.35m, body.FirstPeriodRatePercent);
        Assert.Equal(2.00m, body.MarginPercent);
        Assert.Equal(3.00m, body.EarlyRedemptionFeePerBond);
        Assert.False(body.TaxExempt);
        Assert.Equal(new DateOnly(2036, 10, 1), body.MaturityDate);
        Assert.Equal(5_000m, body.NominalValue);
        Assert.Equal(5_000.00m, body.BookValue);
        Assert.Null(body.FundingAssetId);
        Assert.False(body.IsArchived);
        Assert.Equal(BondStatus.Active, body.Status);
        Assert.Equal(10, body.Periods.Count);

        var asset = await client.GetAssetAsync(portfolioId, body.AssetId, cancellationToken);
        Assert.Equal(AssetClass.Bond, asset.AssetClass);
        Assert.Equal(AssetValuationMode.CurrencyValued, asset.ValuationMode);
        Assert.Equal("PLN", asset.Currency);
        Assert.Equal(5_000.00m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);

        var opening = Assert.Single((await client.ListTransactionsAsync(portfolioId, body.AssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, opening.Type);
        Assert.Equal(5_000.00m, opening.Quantity);
        Assert.Equal(1m, opening.UnitPrice);
        Assert.Equal(new DateOnly(2026, 10, 1), opening.Date);
        Assert.Equal(5_000.00m, opening.ValuePln);
        Assert.Null(opening.Transfer);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, body.AssetId, cancellationToken);

        await using var dbContext = CreateDbContext(userId);
        var terms = await dbContext.Set<TreasuryBond>().SingleAsync(t => t.AssetId == body.AssetId, cancellationToken);
        Assert.Equal(userId, terms.UserId);
        Assert.Equal("EDO1036", terms.SeriesCode);
        Assert.Equal(TreasuryBondType.Edo, terms.Type);
        Assert.Equal(50, terms.BondCount);
        Assert.Equal(new DateOnly(2036, 10, 1), terms.MaturityDate);
    }

    [Fact]
    public async Task Add_FundedFromCash_WritesTransfer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetWithBalanceAsync(walletId, cancellationToken, balance: 6_000m);
        var bondsId = await client.CreatePortfolioAsync(cancellationToken, name: "Bonds");

        var response = await client.PostAsJsonAsync(
            BondsUri(bondsId), NewBondRequest(fundingAssetId: cashId), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(cashId, body.FundingAssetId);
        Assert.Equal("Cash account", body.FundingAssetName);

        Assert.Equal(1_000m, (await client.GetAssetAsync(walletId, cashId, cancellationToken)).Quantity);
        Assert.Equal(5_000m, (await client.GetAssetAsync(bondsId, body.AssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(walletId, cashId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(bondsId, body.AssetId, cancellationToken);

        var withdraw = await client.GetCashWithdrawAsync(walletId, cashId, cancellationToken);
        Assert.Equal(5_000m, withdraw.Quantity);
        Assert.Equal(new DateOnly(2026, 10, 1), withdraw.Date);
        var opening = Assert.Single((await client.ListTransactionsAsync(bondsId, body.AssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, opening.Type);
        Assert.Equal(5_000m, opening.Quantity);
        Assert.Equal(new DateOnly(2026, 10, 1), opening.Date);

        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        Assert.Equal(withdraw.Id, Assert.Single(legs, t => t.AssetId == cashId).Id);
        Assert.Equal(opening.Id, Assert.Single(legs, t => t.AssetId == body.AssetId).Id);
        Assert.Equal(1, await dbContext.Transactions.CountAsync(t => t.AssetId == cashId && t.TransferId == null, cancellationToken));
    }

    [Fact]
    public async Task Add_FundingExceedsBalance_ReturnsInsufficientFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetWithBalanceAsync(walletId, cancellationToken, balance: 4_000m);
        var bondsId = await client.CreatePortfolioAsync(cancellationToken, name: "Bonds");

        var response = await client.PostAsJsonAsync(
            BondsUri(bondsId), NewBondRequest(fundingAssetId: cashId), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        await client.AssertCashUntouchedAsync(walletId, cashId, cancellationToken, balance: 4_000m);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Set<TreasuryBond>().CountAsync(cancellationToken));
        Assert.False(await dbContext.Assets.AnyAsync(a => a.PortfolioId == bondsId, cancellationToken));
    }

    [Fact]
    public async Task Add_StockAsFundingSource_ReturnsInvalidTransferCounterpart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var stockId = await client.AddAssetAsync(walletId, cancellationToken, name: "Shares", assetClass: AssetClass.Stock);
        var bondsId = await client.CreatePortfolioAsync(cancellationToken, name: "Bonds");

        var response = await client.PostAsJsonAsync(
            BondsUri(bondsId), NewBondRequest(fundingAssetId: stockId), cancellationToken);

        await response.AssertInvalidTransferCounterpartAsync(cancellationToken);
        await using var dbContext = CreateDbContext(userId);
        Assert.Equal(0, await dbContext.Set<TreasuryBond>().CountAsync(cancellationToken));
        Assert.False(await dbContext.Assets.AnyAsync(a => a.PortfolioId == bondsId, cancellationToken));
        Assert.False(await dbContext.Transactions.AnyAsync(t => t.TransferId != null, cancellationToken));
    }
}
