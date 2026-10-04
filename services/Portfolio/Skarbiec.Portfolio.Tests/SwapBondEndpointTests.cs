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

// The swap under test is called directly; fixture helpers only arrange.
[Collection(TestingDefaults.CollectionName)]
public sealed class SwapBondEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Swap_Whole_CreatesBondAndPaysLeftover()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var funded = await client.CreateSettledTosAsync(cancellationToken);
        var portfolioId = funded.BondPortfolioId;
        var tosId = funded.Bond.AssetId;

        var response = await client.SwapBondRawAsync(portfolioId, tosId, NewSwapBody(10, funded.CashAssetId), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Swap answered {(int)response.StatusCode}.");

        var edo = await client.FindSwappedBondAsync(tosId, cancellationToken);
        Assert.Equal(portfolioId, edo.PortfolioId);
        Assert.Equal(TreasuryBondType.Edo, edo.Type);
        Assert.Equal("EDO1036", edo.SeriesCode);
        Assert.Equal(TosMaturityDate, edo.PurchaseDate);
        Assert.Equal(10, edo.BondCount);
        Assert.Equal(99.90m, edo.PurchasePricePerBond);
        Assert.Equal(999.00m, edo.BookValue);
        Assert.False(edo.TaxExempt);
        Assert.Equal(tosId, edo.SwappedFrom!.AssetId);
        Assert.Equal("TOS1029", edo.SwappedFrom.Name);

        var edoOpening = Assert.Single((await client.ListTransactionsAsync(portfolioId, edo.AssetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, edoOpening.Type);
        Assert.Equal(999.00m, edoOpening.Quantity);
        Assert.Equal(TosMaturityDate, edoOpening.Date);
        Assert.Equal(tosId, edoOpening.Transfer!.CounterpartAssetId);
        Assert.Equal(TransferDirection.In, edoOpening.Transfer.Direction);

        var tosTransactions = (await client.ListTransactionsAsync(portfolioId, tosId, cancellationToken)).Items;
        var toEdo = Assert.Single(tosTransactions, t => t.Transfer?.CounterpartAssetId == edo.AssetId);
        Assert.Equal(TransactionType.Withdraw, toEdo.Type);
        Assert.Equal(999.00m, toEdo.Quantity);
        var toCash = Assert.Single(
            tosTransactions, t => t.Transfer?.CounterpartAssetId == funded.CashAssetId && t.Transfer.Direction == TransferDirection.Out);
        Assert.Equal(112.69m, toCash.Quantity);
        Assert.Equal(TosMaturityDate, toCash.Date);

        var cashLeg = Assert.Single(
            (await client.ListTransactionsAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Items,
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

        Assert.Equal(0m, (await client.GetAssetAsync(portfolioId, tosId, cancellationToken)).Quantity);
        Assert.Equal(999.00m, (await client.GetAssetAsync(portfolioId, edo.AssetId, cancellationToken)).Quantity);
        Assert.Equal(5_112.69m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        Assert.Equal(BondStatus.Redeemed, (await client.GetBondAsync(portfolioId, tosId, cancellationToken)).Status);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, tosId, cancellationToken);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(portfolioId, edo.AssetId, cancellationToken);
    }

    [Fact]
    public async Task Swap_Part_PaysRestToCash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterTosMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateSettledTosAsync(cancellationToken);
        var tosId = funded.Bond.AssetId;

        var response = await client.SwapBondRawAsync(funded.BondPortfolioId, tosId, NewSwapBody(6, funded.CashAssetId), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Swap answered {(int)response.StatusCode}.");

        var edo = await client.FindSwappedBondAsync(tosId, cancellationToken);
        Assert.Equal(6, edo.BondCount);
        Assert.Equal(599.40m, edo.BookValue);
        Assert.Equal(0m, (await client.GetAssetAsync(funded.BondPortfolioId, tosId, cancellationToken)).Quantity);
        Assert.Equal(5_512.29m, (await client.GetAssetAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken)).Quantity);
        await client.AssertQuantityMatchesRecomputeFromScratchAsync(funded.BondPortfolioId, tosId, cancellationToken);
    }

    public static TheoryData<string, HttpStatusCode, string?> InvalidRequests() => new()
    {
        { "count zero", HttpStatusCode.BadRequest, PortfolioAssertions.BondSwapCountErrorCode },
        { "count above holding", HttpStatusCode.BadRequest, PortfolioAssertions.BondSwapCountErrorCode },
        { "leftover without destination", HttpStatusCode.BadRequest, PortfolioAssertions.BondPayoutDestinationRequiredErrorCode },
        { "series does not match type", HttpStatusCode.BadRequest, null },
        { "cost above proceeds", HttpStatusCode.BadRequest, PortfolioAssertions.BondSwapExceedsProceedsErrorCode },
        { "before maturity", HttpStatusCode.Conflict, PortfolioAssertions.BondNotMaturedErrorCode },
        { "unsettled period", HttpStatusCode.Conflict, PortfolioAssertions.BondInterestUnsettledErrorCode },
        { "already redeemed", HttpStatusCode.Conflict, PortfolioAssertions.BondAlreadyRedeemedErrorCode },
        { "stock as destination", HttpStatusCode.BadRequest, PortfolioAssertions.InvalidTransferCounterpartErrorCode },
        { "archived bond", HttpStatusCode.Conflict, PortfolioAssertions.AssetArchivedErrorCode },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Swap_Invalid_IsRejected(string scenario, HttpStatusCode status, string? errorCode)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(
            scenario == "before maturity" ? BondPurchaseDayUtc
            : scenario == "cost above proceeds" ? AfterRorMaturityUtc
            : AfterTosMaturityUtc);
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
            case "cost above proceeds":
                funded = await client.CreateSettledRorAsync(cancellationToken);
                break;
            default:
                funded = await client.CreateSettledTosAsync(cancellationToken);
                break;
        }

        var portfolioId = funded.BondPortfolioId;
        var assetId = funded.Bond.AssetId;
        var body = NewSwapBody(10, funded.CashAssetId);
        switch (scenario)
        {
            case "count zero":
                body = NewSwapBody(0, funded.CashAssetId);
                break;
            case "count above holding":
                body = NewSwapBody(11, funded.CashAssetId);
                break;
            case "leftover without destination":
                body = NewSwapBody(10, null);
                break;
            case "series does not match type":
                body = NewSwapBody(10, funded.CashAssetId, seriesCode: "COI1030", type: TreasuryBondType.Edo);
                break;
            case "cost above proceeds":
                body = NewSwapBody(10, funded.CashAssetId, swapPricePerBond: 100m);
                break;
            case "already redeemed":
                await client.RedeemBondAsync(portfolioId, assetId, funded.CashAssetId, cancellationToken);
                break;
            case "stock as destination":
                body = NewSwapBody(
                    10, await client.AddAssetAsync(await client.CreatePortfolioAsync(cancellationToken, name: "Stocks"), cancellationToken));
                break;
            case "archived bond":
                await client.ArchiveAssetAsync(portfolioId, assetId, cancellationToken);
                break;
        }

        var rowsBefore = await SnapshotUserRowsAsync(userId, cancellationToken);
        var redemptionsBefore = (await ReadBondRedemptionsAsync(userId, cancellationToken)).Count;

        var response = await client.SwapBondRawAsync(portfolioId, assetId, body, cancellationToken);

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
