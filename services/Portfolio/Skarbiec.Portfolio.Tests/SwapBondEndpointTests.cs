using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The swap under test is called directly; fixture helpers only arrange.
public sealed class SwapBondEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task NewBond_TakesOldGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var group = await client.CreateAssetGroupAsync(cancellationToken);
        var funded = await client.CreateSettledTosAsync(cancellationToken, groupId: group.Id);
        var tosId = funded.Bond.AssetId;

        var response = await client.SwapBondRawAsync(tosId, NewSwapBody(10), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Swap answered {(int)response.StatusCode}.");
        var edo = await client.FindSwappedBondAsync(tosId, cancellationToken);
        Assert.Equal(group.Id, (await ReadAssetGroupStatesAsync(userId, cancellationToken, edo.AssetId))[edo.AssetId].GroupId);
    }

    [Fact]
    public async Task Swap_Whole_CreatesBondAndPaysLeftover()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledTosAsync(cancellationToken);
        var tosId = funded.Bond.AssetId;

        var response = await client.SwapBondRawAsync(tosId, NewSwapBody(10), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Swap answered {(int)response.StatusCode}.");

        var edo = await client.FindSwappedBondAsync(tosId, cancellationToken);
        Assert.Equal(funded.BondAccountId, edo.AccountId);
        Assert.Equal(TreasuryBondType.Edo, edo.Type);
        Assert.Equal("EDO1036", edo.SeriesCode);
        Assert.Equal(TosMaturityDate, edo.PurchaseDate);
        Assert.Equal(10, edo.BondCount);
        Assert.Equal(99.90m, edo.PurchasePricePerBond);
        Assert.Equal(999.00m, edo.BookValue);
        Assert.Equal(TaxWrapper.Regular, edo.TaxWrapper);
        Assert.Equal(tosId, edo.SwappedFrom!.AssetId);
        Assert.Equal("TOS1029", edo.SwappedFrom.Name);

        var edoOpening = Assert.Single((await client.ListTransactionsAsync(edo.AssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, edoOpening.Type);
        Assert.Equal(999.00m, edoOpening.Quantity);
        Assert.Equal(TosMaturityDate, edoOpening.Date);
        Assert.Equal(tosId, edoOpening.Transfer!.CounterpartAssetId);
        Assert.Equal(TransferDirection.In, edoOpening.Transfer.Direction);

        var tosTransactions = (await client.ListTransactionsAsync(tosId, cancellationToken)).Items;
        var toEdo = Assert.Single(tosTransactions, t => t.Transfer?.CounterpartAssetId == edo.AssetId);
        Assert.Equal(TransactionType.Withdraw, toEdo.Type);
        Assert.Equal(999.00m, toEdo.Quantity);
        var toCash = Assert.Single(
            tosTransactions, t => t.Transfer?.CounterpartAssetId == funded.CashAssetId && t.Transfer.Direction == TransferDirection.Out);
        Assert.Equal(112.69m, toCash.Quantity);
        Assert.Equal(TosMaturityDate, toCash.Date);

        var cashLeg = Assert.Single(
            (await client.ListTransactionsAsync(funded.CashAssetId, cancellationToken)).Items,
            t => t.Transfer is not null && t.Transfer.Direction == TransferDirection.In);
        Assert.Equal(112.69m, cashLeg.Quantity);
        Assert.Equal(TosMaturityDate, cashLeg.Date);

        var row = Assert.Single(await ReadBondRedemptionsAsync(userId, cancellationToken, tosId));
        Assert.Equal("Swap", row.Kind.ToString());
        Assert.Equal(edo.AssetId, row.SwapTargetAssetId);
        Assert.NotNull(row.SwapTransferId);
        Assert.NotNull(row.CashTransferId);
        Assert.Equal(10, row.BondCount);
        Assert.Equal(26.21m, row.Tax);
        Assert.Equal(1_111.69m, row.Proceeds);

        Assert.Equal(0m, (await client.GetAssetAsync(tosId, cancellationToken)).Quantity);
        Assert.Equal(999.00m, (await client.GetAssetAsync(edo.AssetId, cancellationToken)).Quantity);
        Assert.Equal(5_112.69m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(BondStatus.Redeemed, (await client.GetBondAsync(tosId, cancellationToken)).Status);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(tosId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(edo.AssetId, cancellationToken);
    }

    [Fact]
    public async Task Leftover_ToAccountCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateSettledTosAsync(cancellationToken);
        var tosId = funded.Bond.AssetId;

        var response = await client.SwapBondRawAsync(tosId, NewSwapBody(8), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Swap answered {(int)response.StatusCode}.");

        var edo = await client.FindSwappedBondAsync(tosId, cancellationToken);
        Assert.Equal(funded.BondAccountId, edo.AccountId);
        Assert.Equal(8, edo.BondCount);
        Assert.Equal(799.20m, edo.BookValue);
        var leftover = Assert.Single(
            (await client.ListTransactionsAsync(funded.CashAssetId, cancellationToken)).Items,
            t => t.Transfer is { Direction: TransferDirection.In } && t.Transfer.CounterpartAssetId == tosId);
        Assert.Equal(312.49m, leftover.Quantity);
        Assert.Equal(TosMaturityDate, leftover.Date);
        Assert.Equal(0m, (await client.GetAssetAsync(tosId, cancellationToken)).Quantity);
        Assert.Equal(5_312.49m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(tosId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.CashAssetId, cancellationToken);
    }

    [Fact]
    public async Task Shortfall_FromAccountCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        // A discounted ROR pays out 981 for 10 bonds bought at 90; 10 bonds at the 100 swap price cost 1 000.
        Factory.Clock.SetUtcNow(AfterRorMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledRorAsync(cancellationToken, purchasePricePerBond: 90m);
        var rorId = funded.Bond.AssetId;
        var cashBefore = (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity;

        var response = await client.SwapBondRawAsync(rorId, NewSwapBody(10, swapPricePerBond: 100m), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Swap answered {(int)response.StatusCode}.");

        var edo = await client.FindSwappedBondAsync(rorId, cancellationToken);
        Assert.Equal(funded.BondAccountId, edo.AccountId);
        Assert.Equal(1_000.00m, edo.BookValue);
        var edoLegs = (await client.ListTransactionsAsync(edo.AssetId, cancellationToken)).Items;
        Assert.All(edoLegs, t => Assert.Equal(RorMaturityDate, t.Date));
        Assert.Equal(981.00m, Assert.Single(edoLegs, t => t.Transfer?.CounterpartAssetId == rorId).Quantity);
        var shortfall = Assert.Single(edoLegs, t => t.Transfer?.CounterpartAssetId == funded.CashAssetId);
        Assert.Equal(TransactionType.Deposit, shortfall.Type);
        Assert.Equal(19.00m, shortfall.Quantity);

        var cashOut = Assert.Single(
            (await client.ListTransactionsAsync(funded.CashAssetId, cancellationToken)).Items,
            t => t.Transfer?.CounterpartAssetId == edo.AssetId);
        Assert.Equal(TransactionType.Withdraw, cashOut.Type);
        Assert.Equal(19.00m, cashOut.Quantity);
        Assert.Equal(RorMaturityDate, cashOut.Date);
        Assert.Null(Assert.Single(await ReadBondRedemptionsAsync(userId, cancellationToken, rorId)).CashTransferId);

        Assert.Equal(1_000.00m, (await client.GetAssetAsync(edo.AssetId, cancellationToken)).Quantity);
        Assert.Equal(cashBefore - 19.00m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(edo.AssetId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.CashAssetId, cancellationToken);
    }

    [Fact]
    public async Task Shortfall_ExceedsCash_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterRorMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledRorAsync(cancellationToken, purchasePricePerBond: 90m);
        var cashBefore = (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity;
        await client.RecordTransactionAsync(
            funded.CashAssetId, TransactionType.Withdraw, cashBefore - 5m, RorMaturityDate, cancellationToken, unitPrice: 1m);
        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);

        var response = await client.SwapBondRawAsync(
            funded.Bond.AssetId, NewSwapBody(10, swapPricePerBond: 100m), cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Empty(await ReadBondRedemptionsAsync(userId, cancellationToken, funded.Bond.AssetId));
        Assert.Equal(5m, (await client.GetAssetAsync(funded.CashAssetId, cancellationToken)).Quantity);
    }

    public static TheoryData<string, HttpStatusCode, string?> InvalidRequests() => new()
    {
        { "count zero", HttpStatusCode.BadRequest, PortfolioAssertions.BondSwapCountErrorCode },
        { "count above holding", HttpStatusCode.BadRequest, PortfolioAssertions.BondSwapCountErrorCode },
        { "series does not match type", HttpStatusCode.BadRequest, null },
        { "already redeemed", HttpStatusCode.Conflict, PortfolioAssertions.BondAlreadyRedeemedErrorCode },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Swap_Invalid_IsRejected(string scenario, HttpStatusCode status, string? errorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledTosAsync(cancellationToken);

        var assetId = funded.Bond.AssetId;
        var body = NewSwapBody(10);
        switch (scenario)
        {
            case "count zero":
                body = NewSwapBody(0);
                break;
            case "count above holding":
                body = NewSwapBody(11);
                break;
            case "series does not match type":
                body = NewSwapBody(10, seriesCode: "COI1030", type: TreasuryBondType.Edo);
                break;
            case "already redeemed":
                await client.RedeemBondAsync(assetId, cancellationToken);
                break;
        }

        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);
        var redemptionsBefore = (await ReadBondRedemptionsAsync(userId, cancellationToken)).Count;

        var response = await client.SwapBondRawAsync(assetId, body, cancellationToken);

        if (errorCode is null)
        {
            Assert.Equal(status, response.StatusCode);
        }
        else
        {
            await response.AssertProblemAsync(status, errorCode, cancellationToken);
        }

        Assert.Equal(rowsBefore, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(redemptionsBefore, (await ReadBondRedemptionsAsync(userId, cancellationToken)).Count);
    }
}
