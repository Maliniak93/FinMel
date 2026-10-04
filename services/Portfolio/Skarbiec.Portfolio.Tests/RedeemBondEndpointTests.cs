using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The redemption under test is called directly; fixture helpers only arrange.
[Collection(TestingDefaults.CollectionName)]
public sealed class RedeemBondEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Redeem_Capitalising_TaxesInterestAtMaturity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledTosAsync(cancellationToken);
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        Assert.Equal(1_137.90m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);

        var response = await client.RedeemBondRawAsync(portfolioId, assetId, funded.CashAssetId, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Redemption answered {(int)response.StatusCode}.");

        var row = Assert.Single(await ReadBondRedemptionsAsync(userId, cancellationToken, assetId));
        Assert.Equal("Maturity", row.Kind.ToString());
        Assert.Equal(TosMaturityDate, row.Date);
        Assert.Equal(10, row.BondCount);
        Assert.Equal(137.90m, row.CapitalisedInterest);
        Assert.Equal(0m, row.DiscountIncome);
        Assert.Equal(26.21m, row.Tax);
        Assert.Equal(1_111.69m, row.Proceeds);
        Assert.Null(row.CreditTransactionId);
        Assert.NotNull(row.ChargeTransactionId);
        Assert.NotNull(row.CashTransferId);
        Assert.Null(row.SwapTargetAssetId);
        Assert.Null(row.SwapTransferId);

        var bondTransactions = (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items;
        var charge = Assert.Single(bondTransactions, t => t.Type == TransactionType.Withdraw && t.Transfer is null);
        Assert.Equal(26.21m, charge.Quantity);
        Assert.Equal(TosMaturityDate, charge.Date);
        var payout = Assert.Single(bondTransactions, t => t.Transfer?.Direction == TransferDirection.Out);
        Assert.Equal(TransactionType.Withdraw, payout.Type);
        Assert.Equal(1_111.69m, payout.Quantity);
        Assert.Equal(TosMaturityDate, payout.Date);
        Assert.Equal(funded.CashAssetId, payout.Transfer!.CounterpartAssetId);
        Assert.Equal(TransferDirection.Out, payout.Transfer.Direction);

        var cashLeg = Assert.Single(
            (await client.ListTransactionsAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Items,
            t => t.Transfer is not null && t.Transfer.Direction == TransferDirection.In);
        Assert.Equal(1_111.69m, cashLeg.Quantity);
        Assert.Equal(TosMaturityDate, cashLeg.Date);
        Assert.Equal(assetId, cashLeg.Transfer!.CounterpartAssetId);

        Assert.Equal(0m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        Assert.Equal(6_111.69m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, assetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);

        var bond = await client.GetBondAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(BondStatus.Redeemed, bond.Status);
        Assert.Equal(0m, bond.BookValue);
    }

    [Theory]
    [InlineData(false, 0.19, 999.81)]
    [InlineData(true, 0, 1_000.00)]
    public async Task Redeem_SwapBought_TaxesDiscount(bool taxExempt, double expectedTax, double expectedProceeds)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tax = (decimal)expectedTax;
        var proceeds = (decimal)expectedProceeds;
        Factory.Clock.SetUtcNow(AfterRorMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledRorAsync(cancellationToken, taxExempt: taxExempt);
        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        var cashBefore = (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity;

        var response = await client.RedeemBondRawAsync(portfolioId, assetId, funded.CashAssetId, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Redemption answered {(int)response.StatusCode}.");

        var row = Assert.Single(await ReadBondRedemptionsAsync(userId, cancellationToken, assetId));
        Assert.Equal(0m, row.CapitalisedInterest);
        Assert.Equal(1.00m, row.DiscountIncome);
        Assert.Equal(tax, row.Tax);
        Assert.Equal(proceeds, row.Proceeds);
        Assert.NotNull(row.CreditTransactionId);
        Assert.Equal(tax > 0, row.ChargeTransactionId is not null);

        var bondTransactions = (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items;
        var credit = Assert.Single(
            bondTransactions,
            t => t is { Type: TransactionType.Deposit, Transfer: null, BondInterestPeriodIndex: null } && t.Date == RorMaturityDate);
        Assert.Equal(1.00m, credit.Quantity);
        var charges = bondTransactions.Where(t => t is { Type: TransactionType.Withdraw, Transfer: null }).ToList();
        if (tax > 0)
        {
            Assert.Equal(tax, Assert.Single(charges).Quantity);
        }
        else
        {
            Assert.Empty(charges);
        }

        Assert.Equal(0m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        Assert.Equal(
            cashBefore + proceeds,
            (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, assetId, cancellationToken);
    }

    public static TheoryData<string, HttpStatusCode, string> InvalidRequests() => new()
    {
        { "before maturity", HttpStatusCode.Conflict, PortfolioAssertions.BondNotMaturedErrorCode },
        { "unsettled period", HttpStatusCode.Conflict, PortfolioAssertions.BondInterestUnsettledErrorCode },
        { "already redeemed", HttpStatusCode.Conflict, PortfolioAssertions.BondAlreadyRedeemedErrorCode },
        { "stock as destination", HttpStatusCode.BadRequest, PortfolioAssertions.InvalidTransferCounterpartErrorCode },
        { "archived bond", HttpStatusCode.Conflict, PortfolioAssertions.AssetArchivedErrorCode },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Redeem_Invalid_IsRejected(string scenario, HttpStatusCode status, string errorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(scenario == "before maturity" ? BondPurchaseDayUtc : AfterTosMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        FundedBond funded;
        switch (scenario)
        {
            case "before maturity":
                funded = await client.CreateFundedBondAsync(cancellationToken, request: NewTosBondRequest());
                break;
            case "unsettled period":
                funded = await client.CreateFundedBondAsync(cancellationToken, request: NewTosBondRequest());
                await client.SettleBondInterestAsync(
                    funded.BondPortfolioId, funded.Bond.AssetId, [(1, null)], null, cancellationToken);
                break;
            default:
                funded = await client.CreateSettledTosAsync(cancellationToken);
                break;
        }

        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        var destination = funded.CashAssetId;
        switch (scenario)
        {
            case "already redeemed":
                await client.RedeemBondAsync(portfolioId, assetId, destination, cancellationToken);
                break;
            case "stock as destination":
                destination = await client.AddAssetAsync(await client.CreatePortfolioAsync(cancellationToken, name: "Stocks"), cancellationToken);
                break;
            case "archived bond":
                await client.ArchiveAssetAsync(portfolioId, assetId, cancellationToken);
                break;
        }

        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);
        var redemptionsBefore = (await ReadBondRedemptionsAsync(userId, cancellationToken)).Count;

        var response = await client.RedeemBondRawAsync(portfolioId, assetId, destination, cancellationToken);

        await response.AssertProblemAsync(status, errorCode, cancellationToken);
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(redemptionsBefore, (await ReadBondRedemptionsAsync(userId, cancellationToken)).Count);
    }
}
