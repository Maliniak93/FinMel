using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The redemption under test is called directly; fixture helpers only arrange.
public sealed class RedeemBondEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task ProceedsToAccountCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledTosAsync(cancellationToken);
        var assetId = funded.Bond.AssetId;
        Assert.Equal(1_137.90m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);

        var response = await client.RedeemBondRawAsync(assetId, cancellationToken);

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

        var bondTransactions = (await client.ListTransactionsAsync(assetId, cancellationToken)).Items;
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
            (await client.ListTransactionsAsync(funded.CashAssetId, cancellationToken)).Items,
            t => t.Transfer is not null && t.Transfer.Direction == TransferDirection.In);
        Assert.Equal(1_111.69m, cashLeg.Quantity);
        Assert.Equal(TosMaturityDate, cashLeg.Date);
        Assert.Equal(assetId, cashLeg.Transfer!.CounterpartAssetId);

        Assert.Equal(0m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        Assert.Equal(6_111.69m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(assetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.CashAssetId, cancellationToken);

        var bond = await client.GetBondAsync(assetId, cancellationToken);
        Assert.Equal(BondStatus.Redeemed, bond.Status);
        Assert.Equal(0m, bond.BookValue);
    }

    [Theory]
    [InlineData(TaxWrapper.Regular, 0.19, 999.81)]
    [InlineData(TaxWrapper.Ike, 0, 1_000.00)]
    public async Task Redeem_SwapBought_TaxesDiscount(TaxWrapper wrapper, double expectedTax, double expectedProceeds)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var tax = (decimal)expectedTax;
        var proceeds = (decimal)expectedProceeds;
        Factory.Clock.SetUtcNow(AfterRorMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledRorAsync(cancellationToken, wrapper: wrapper);
        var assetId = funded.Bond.AssetId;
        var cashBefore = (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity;

        var response = await client.RedeemBondRawAsync(assetId, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Redemption answered {(int)response.StatusCode}.");

        var row = Assert.Single(await ReadBondRedemptionsAsync(userId, cancellationToken, assetId));
        Assert.Equal(0m, row.CapitalisedInterest);
        Assert.Equal(1.00m, row.DiscountIncome);
        Assert.Equal(tax, row.Tax);
        Assert.Equal(proceeds, row.Proceeds);
        Assert.NotNull(row.CreditTransactionId);
        Assert.Equal(tax > 0, row.ChargeTransactionId is not null);

        var bondTransactions = (await client.ListTransactionsAsync(assetId, cancellationToken)).Items;
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

        Assert.Equal(0m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        Assert.Equal(
            cashBefore + proceeds,
            (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(assetId, cancellationToken);
    }

    [Fact]
    public async Task Redeem_AfterPartialEarly_UsesRemainingCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var earlyDate = new DateOnly(2027, 3, 12);
        Factory.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 12, 10, 0, 0, TimeSpan.Zero));
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateFundedBondAsync(cancellationToken, request: NewTosBondRequest());
        var assetId = funded.Bond.AssetId;
        await client.RedeemBondEarlyAsync(assetId, earlyDate, 4, cancellationToken);
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        await client.SettleBondInterestAsync(assetId, [(1, null), (2, null), (3, null)], cancellationToken);
        var cashBefore = (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity;

        var response = await client.RedeemBondRawAsync(assetId, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Redemption answered {(int)response.StatusCode}.");
        var rows = await ReadBondRedemptionsAsync(userId, cancellationToken, assetId);
        var row = Assert.Single(rows, r => r.Kind.ToString() == "Maturity");
        Assert.Equal(6, row.BondCount);
        Assert.Equal(82.74m, row.CapitalisedInterest);
        Assert.Equal(15.73m, row.Tax);
        Assert.Equal(667.01m, row.Proceeds);
        Assert.Equal(0m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        Assert.Equal(
            cashBefore + 667.01m,
            (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(assetId, cancellationToken);
    }

    public static TheoryData<string, HttpStatusCode, string> InvalidRequests() => new()
    {
        { "before maturity", HttpStatusCode.Conflict, PortfolioAssertions.BondNotMaturedErrorCode },
        { "unsettled period", HttpStatusCode.Conflict, PortfolioAssertions.BondInterestUnsettledErrorCode },
        { "already redeemed", HttpStatusCode.Conflict, PortfolioAssertions.BondAlreadyRedeemedErrorCode },
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
                    funded.Bond.AssetId, [(1, null)], cancellationToken);
                break;
            default:
                funded = await client.CreateSettledTosAsync(cancellationToken);
                break;
        }

        var assetId = funded.Bond.AssetId;
        switch (scenario)
        {
            case "already redeemed":
                await client.RedeemBondAsync(assetId, cancellationToken);
                break;
            case "archived bond":
                await client.ArchiveAssetAsync(assetId, cancellationToken);
                break;
        }

        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);
        var redemptionsBefore = (await ReadBondRedemptionsAsync(userId, cancellationToken)).Count;

        var response = await client.RedeemBondRawAsync(assetId, cancellationToken);

        await response.AssertProblemAsync(status, errorCode, cancellationToken);
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(redemptionsBefore, (await ReadBondRedemptionsAsync(userId, cancellationToken)).Count);
    }
}
