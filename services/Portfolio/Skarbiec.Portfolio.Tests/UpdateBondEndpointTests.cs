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
public sealed class UpdateBondEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Update_RewritesOpeningAndFundingLegs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(cancellationToken, cashBalance: 6_000m);
        var openingId = Assert.Single((await client.ListTransactionsAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken)).Items).Id;
        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);
        var request = NewBondRequest(
            name: "Renamed bond",
            bondCount: 60,
            purchasePricePerBond: 99.90m,
            purchaseDate: new DateOnly(2026, 9, 15)).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(
            BondUri(funded.BondPortfolioId, funded.Bond.AssetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BondResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("Renamed bond", body.Name);
        Assert.Equal(60, body.BondCount);
        Assert.Equal(99.90m, body.PurchasePricePerBond);
        Assert.Equal(new DateOnly(2026, 9, 15), body.PurchaseDate);
        Assert.Equal(new DateOnly(2036, 9, 15), body.MaturityDate);
        Assert.Equal(6_000m, body.NominalValue);
        Assert.Equal(5_994.00m, body.BookValue);
        Assert.Equal(funded.CashAssetId, body.FundingAssetId);

        Assert.Equal(6m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(5_994.00m, (await client.GetAssetAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken);

        var opening = Assert.Single((await client.ListTransactionsAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken)).Items);
        Assert.Equal(openingId, opening.Id);
        Assert.Equal(5_994.00m, opening.Quantity);
        Assert.Equal(new DateOnly(2026, 9, 15), opening.Date);
        var withdraw = await client.GetCashWithdrawAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        Assert.Equal(5_994.00m, withdraw.Quantity);
        Assert.Equal(new DateOnly(2026, 9, 15), withdraw.Date);

        var rowsAfter = await SnapshotUserRowsAsync(userId, cancellationToken);
        Assert.Equal(rowsBefore.Assets, rowsAfter.Assets);
        Assert.Equal(rowsBefore.Transactions, rowsAfter.Transactions);

        await using var dbContext = CreateDbContext(userId);
        var legs = await dbContext.Transactions.Where(t => t.TransferId != null).ToListAsync(cancellationToken);
        Assert.Equal(2, legs.Count);
        Assert.Single(legs.Select(t => t.TransferId).Distinct());
        var terms = await dbContext.Set<TreasuryBond>().SingleAsync(t => t.AssetId == funded.Bond.AssetId, cancellationToken);
        Assert.Equal(60, terms.BondCount);
        Assert.Equal(new DateOnly(2036, 9, 15), terms.MaturityDate);
    }

    [Fact]
    public async Task Update_Archived_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken);
        await client.ArchiveAssetAsync(portfolioId, bond.AssetId, cancellationToken);
        var request = NewBondRequest(name: "After archive", bondCount: 10).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(BondUri(portfolioId, bond.AssetId), request, cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetBondAsync(portfolioId, bond.AssetId, cancellationToken);
        Assert.Equal("EDO1036", unchanged.Name);
        Assert.Equal(50, unchanged.BondCount);
    }

    [Fact]
    public async Task Update_ArchivedPortfolio_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
        var request = NewBondRequest(name: "After archive", bondCount: 10).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(BondUri(portfolioId, bond.AssetId), request, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        var unchanged = await client.GetBondAsync(portfolioId, bond.AssetId, cancellationToken);
        Assert.Equal("EDO1036", unchanged.Name);
        Assert.Equal(50, unchanged.BondCount);
    }
}
