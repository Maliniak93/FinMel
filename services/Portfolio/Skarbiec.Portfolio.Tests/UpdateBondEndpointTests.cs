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

    [Fact]
    public async Task Update_Settled_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewRorBondRequest());
        await client.SettleBondInterestAsync(funded.BondPortfolioId, funded.Bond.AssetId, [(1, null)], funded.CashAssetId, cancellationToken);
        var request = NewRorBondRequest(bondCount: 10).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(BondUri(funded.BondPortfolioId, funded.Bond.AssetId), request, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BondSettledErrorCode, cancellationToken);
        var unchanged = await client.GetBondAsync(funded.BondPortfolioId, funded.Bond.AssetId, cancellationToken);
        Assert.Equal(50, unchanged.BondCount);
    }

    [Fact]
    public async Task Update_Redeemed_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 1, 10, 0, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateFundedBondAsync(cancellationToken);
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        await client.RedeemBondEarlyAsync(portfolioId, assetId, new DateOnly(2027, 3, 1), 3, funded.CashAssetId, cancellationToken);
        var request = NewBondRequest(bondCount: 40).ToUpdateRequest();

        var response = await client.PutAsJsonAsync(BondUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BondRedeemedErrorCode, cancellationToken);
        Assert.Equal(47, (await client.GetBondAsync(portfolioId, assetId, cancellationToken)).BondCount);
    }

    [Fact]
    public async Task Update_SwapBorn_KeepsSwapTermsFixed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateSettledTosAsync(cancellationToken);
        await client.SwapBondAsync(funded.BondPortfolioId, funded.Bond.AssetId, 10, funded.CashAssetId, cancellationToken);
        var edo = await client.FindSwappedBondAsync(funded.Bond.AssetId, cancellationToken);
        var current = edo.ToUpdateRequest();
        var uri = BondUri(edo.PortfolioId, edo.AssetId);
        var forbidden = new[]
        {
            current with { BondCount = 9 },
            current with { PurchasePricePerBond = 100m },
            current with { PurchaseDate = new DateOnly(2029, 9, 15) },
            current with { SeriesCode = "EDO1136" },
            current with { Type = TreasuryBondType.Coi, SeriesCode = "COI1030" },
        };

        foreach (var request in forbidden)
        {
            var rejected = await client.PutAsJsonAsync(uri, request, cancellationToken);

            await rejected.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.BondFromSwapErrorCode, cancellationToken);
        }

        var unchanged = await client.GetBondAsync(edo.PortfolioId, edo.AssetId, cancellationToken);
        Assert.Equal(10, unchanged.BondCount);
        Assert.Equal(99.90m, unchanged.PurchasePricePerBond);
        Assert.Equal(TosMaturityDate, unchanged.PurchaseDate);
        Assert.Equal(999.00m, unchanged.BookValue);

        var allowed = await client.PutAsJsonAsync(
            uri,
            current with { Name = "Renamed EDO", FirstPeriodRatePercent = 5.50m, MarginPercent = 1.75m, EarlyRedemptionFeePerBond = 2.00m },
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var updated = await client.GetBondAsync(edo.PortfolioId, edo.AssetId, cancellationToken);
        Assert.Equal("Renamed EDO", updated.Name);
        Assert.Equal(5.50m, updated.FirstPeriodRatePercent);
        Assert.Equal(1.75m, updated.MarginPercent);
        Assert.Equal(2.00m, updated.EarlyRedemptionFeePerBond);
        Assert.Equal(999.00m, updated.BookValue);
        Assert.Equal(funded.Bond.AssetId, updated.SwappedFrom!.AssetId);
    }
}
