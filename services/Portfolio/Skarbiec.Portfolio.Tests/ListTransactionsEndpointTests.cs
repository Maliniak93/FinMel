using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ListTransactionsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ReturnsOnlyTransactionsOfThatAsset_NewestFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(portfolioId, cancellationToken, name: "Asset A");
        var otherAssetId = await client.AddAssetAsync(portfolioId, cancellationToken, name: "Asset B");

        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 10), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, otherAssetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 15), cancellationToken);

        var response = await client.GetAsync(TransactionsUri(portfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(cancellationToken);
        Assert.NotNull(page);
        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, t => Assert.Equal(assetId, t.AssetId));
        Assert.Equal(new DateOnly(2026, 1, 10), page.Items[0].Date);
        Assert.Equal(new DateOnly(2026, 1, 1), page.Items[1].Date);
    }

    [Fact]
    public async Task List_Paged_ReturnsRequestedPageAndTotalCount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(portfolioId, cancellationToken, name: "Asset A");

        for (var day = 1; day <= 5; day++)
        {
            await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, day), cancellationToken);
        }

        var response = await client.GetAsync($"{TransactionsUri(portfolioId, assetId)}?page=2&pageSize=2", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(cancellationToken);
        Assert.NotNull(page);
        Assert.Equal(5, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(new DateOnly(2026, 1, 3), page.Items[0].Date);
        Assert.Equal(new DateOnly(2026, 1, 2), page.Items[1].Date);
    }

    [Fact]
    public async Task List_ReturnsCurrencyAndValuePln()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var pricedDate = new DateOnly(2026, 3, 4);
        var unpricedDate = new DateOnly(2020, 1, 2);
        Factory.FxRateLookupClient.WithRate("EUR", pricedDate, 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken, currency: "EUR");
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 2m, pricedDate, cancellationToken, unitPrice: 50m);
        Factory.FxRateLookupClient.WithNotFound("EUR");
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 1m, unpricedDate, cancellationToken, unitPrice: 50m);

        var response = await client.GetAsync(TransactionsUri(portfolioId, assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.ReadJsonAsync(cancellationToken)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, item =>
        {
            item.AssertCarriesNoFee();
            Assert.Equal("EUR", item.GetProperty("currency").GetString());
        });
        var priced = items[0].Deserialize<TransactionResponse>(JsonSerializerOptions.Web)!;
        var unpriced = items[1].Deserialize<TransactionResponse>(JsonSerializerOptions.Web)!;
        Assert.Equal(pricedDate, priced.Date);
        Assert.Equal(430.00m, priced.ValuePln);
        Assert.Equal(unpricedDate, unpriced.Date);
        Assert.Null(unpriced.ValuePln);
    }

    [Fact]
    public async Task List_TransferLeg_ReturnsCounterpart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateFundedDepositAsync(cancellationToken);

        var cashResponse = await client.GetAsync(TransactionsUri(funded.CashPortfolioId, funded.CashAssetId), cancellationToken);
        var depositResponse = await client.GetAsync(TransactionsUri(funded.DepositPortfolioId, funded.Deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, cashResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, depositResponse.StatusCode);
        var cashItems = (await cashResponse.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(cancellationToken))!.Items;
        var depositItems = (await depositResponse.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(cancellationToken))!.Items;

        var outLeg = Assert.Single(cashItems, t => t.Type == TransactionType.Withdraw);
        Assert.NotNull(outLeg.Transfer);
        Assert.Equal(funded.Deposit.AssetId, outLeg.Transfer.CounterpartAssetId);
        Assert.Equal("Term deposit", outLeg.Transfer.CounterpartAssetName);
        Assert.Equal(funded.DepositPortfolioId, outLeg.Transfer.CounterpartPortfolioId);
        Assert.Equal("Savings", outLeg.Transfer.CounterpartPortfolioName);
        Assert.Equal(TransferDirection.Out, outLeg.Transfer.Direction);

        var plain = Assert.Single(cashItems, t => t.Type == TransactionType.Deposit);
        Assert.Null(plain.Transfer);

        var inLeg = Assert.Single(depositItems);
        Assert.NotNull(inLeg.Transfer);
        Assert.Equal(funded.CashAssetId, inLeg.Transfer.CounterpartAssetId);
        Assert.Equal("Cash account", inLeg.Transfer.CounterpartAssetName);
        Assert.Equal(funded.CashPortfolioId, inLeg.Transfer.CounterpartPortfolioId);
        Assert.Equal("Wallet", inLeg.Transfer.CounterpartPortfolioName);
        Assert.Equal(TransferDirection.In, inLeg.Transfer.Direction);
    }

    [Fact]
    public async Task List_ForNonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.GetAsync(TransactionsUri(portfolioId, Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(TransactionsUri(Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_SavingsInterestCredit_ReturnsPeriodEnd()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken, NewInterestAccountRequest());
        await client.SettlePreviewedSavingsInterestAsync(portfolioId, account.AssetId, cancellationToken);

        var response = await client.GetAsync(TransactionsUri(portfolioId, account.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.ReadJsonAsync(cancellationToken)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var credit = Assert.Single(items, i => i.GetProperty("quantity").GetDecimal() == 33.29m);
        Assert.Equal("2026-09-30", credit.GetProperty("savingsInterestPeriodEnd").GetString());
        var opening = Assert.Single(items, i => i.GetProperty("quantity").GetDecimal() == 10_000m);
        Assert.True(!opening.TryGetProperty("savingsInterestPeriodEnd", out var value) || value.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task List_ManualTransferLeg_IsManual()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateCashAndSavingsAsync(cancellationToken);
        var transferId = await client.CreateTransferAsync(cancellationToken, NewTransferRequest(setup.CashAssetId, setup.SavingsAssetId, 2_000m));

        var cashLeg = await client.GetCashWithdrawAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken);
        var savingsLeg = Assert.Single((await client.ListTransactionsAsync(setup.SavingsPortfolioId, setup.SavingsAssetId, cancellationToken)).Items);

        Assert.NotNull(cashLeg.Transfer);
        Assert.Equal(transferId, cashLeg.Transfer.TransferId);
        Assert.True(cashLeg.Transfer.Manual);
        Assert.NotNull(savingsLeg.Transfer);
        Assert.Equal(transferId, savingsLeg.Transfer.TransferId);
        Assert.True(savingsLeg.Transfer.Manual);
        Assert.Equal(TransferDirection.In, savingsLeg.Transfer.Direction);

        using var fundingClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await fundingClient.CreateFundedDepositAsync(cancellationToken);
        var fundingLeg = await fundingClient.GetCashWithdrawAsync(funded.CashPortfolioId, funded.CashAssetId, cancellationToken);
        Assert.NotNull(fundingLeg.Transfer);
        Assert.NotEqual(Guid.Empty, fundingLeg.Transfer.TransferId);
        Assert.False(fundingLeg.Transfer.Manual);
    }

    [Fact]
    public async Task MetalCashLegs_CarryCounterpartAndDirection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateCashAndMetalAsync(cancellationToken);
        var buy = await client.RecordMetalTransactionWithCashAsync(setup.MetalPortfolioId, setup.MetalAssetId, setup.CashAssetId, cancellationToken);
        var sell = await client.RecordMetalTransactionWithCashAsync(
            setup.MetalPortfolioId, setup.MetalAssetId, setup.CashAssetId, cancellationToken,
            type: TransactionType.Sell, pieces: 1m, pricePerPiece: 1_300m, date: MetalCashDate.AddDays(1));

        var cashLegs = (await client.ListTransactionsAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Items;
        var withdraw = Assert.Single(cashLegs, t => t.Type == TransactionType.Withdraw);
        var deposit = Assert.Single(cashLegs, t => t.Type == TransactionType.Deposit && t.Transfer is not null);

        Assert.NotNull(buy.Transfer);
        Assert.Equal(setup.CashAssetId, buy.Transfer.CounterpartAssetId);
        Assert.Equal(TransferDirection.In, buy.Transfer.Direction);
        Assert.True(buy.Transfer.Manual);
        Assert.NotNull(withdraw.Transfer);
        Assert.Equal(buy.Transfer.TransferId, withdraw.Transfer.TransferId);
        Assert.Equal(setup.MetalAssetId, withdraw.Transfer.CounterpartAssetId);
        Assert.Equal(TransferDirection.Out, withdraw.Transfer.Direction);
        Assert.True(withdraw.Transfer.Manual);
        Assert.NotNull(sell.Transfer);
        Assert.Equal(TransferDirection.Out, sell.Transfer.Direction);
        Assert.True(sell.Transfer.Manual);
        Assert.NotNull(deposit.Transfer);
        Assert.Equal(sell.Transfer.TransferId, deposit.Transfer.TransferId);
        Assert.Equal(TransferDirection.In, deposit.Transfer.Direction);
        Assert.True(deposit.Transfer.Manual);
    }

    [Fact]
    public async Task SecurityTrade_ShowsCounterparts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await CreateCashAndEtfAsync(client, cancellationToken);
        await client.RecordTradeWithCashAsync(setup.EtfPortfolioId, setup.EtfAssetId, setup.CashAssetId, cancellationToken);

        var buy = Assert.Single((await client.ListTransactionsAsync(setup.EtfPortfolioId, setup.EtfAssetId, cancellationToken)).Items);
        var withdraw = Assert.Single(
            (await client.ListTransactionsAsync(setup.CashPortfolioId, setup.CashAssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);

        Assert.NotNull(buy.Transfer);
        Assert.Equal(setup.CashAssetId, buy.Transfer.CounterpartAssetId);
        Assert.Equal(setup.CashPortfolioId, buy.Transfer.CounterpartPortfolioId);
        Assert.Equal(TransferDirection.In, buy.Transfer.Direction);
        Assert.False(buy.Transfer.Manual);
        Assert.NotNull(withdraw.Transfer);
        Assert.Equal(buy.Transfer.TransferId, withdraw.Transfer.TransferId);
        Assert.Equal(setup.EtfAssetId, withdraw.Transfer.CounterpartAssetId);
        Assert.Equal(setup.EtfPortfolioId, withdraw.Transfer.CounterpartPortfolioId);
        Assert.Equal(TransferDirection.Out, withdraw.Transfer.Direction);
        Assert.False(withdraw.Transfer.Manual);
    }

    [Fact]
    public async Task List_DepositPayoutLegOnSavings_IsNotManual()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateDepositPaidIntoSavingsAsync(cancellationToken);

        var savingsLeg = Assert.Single((await client.ListTransactionsAsync(setup.SavingsPortfolioId, setup.Account.AssetId, cancellationToken)).Items);
        var depositLeg = Assert.Single(
            (await client.ListTransactionsAsync(setup.DepositPortfolioId, setup.Deposit.AssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);

        Assert.NotNull(savingsLeg.Transfer);
        Assert.False(savingsLeg.Transfer.Manual);
        Assert.Equal(TransferDirection.In, savingsLeg.Transfer.Direction);
        Assert.NotNull(depositLeg.Transfer);
        Assert.False(depositLeg.Transfer.Manual);
        Assert.Equal(savingsLeg.Transfer.TransferId, depositLeg.Transfer.TransferId);
    }

    [Fact]
    public async Task List_BondInterestCredit_CarriesPeriod()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2027, 10, 2, 10, 0, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, bond) = await client.CreatePortfolioWithBondAsync(cancellationToken);
        await client.SettleBondInterestAsync(portfolioId, bond.AssetId, [(1, null)], null, cancellationToken);

        var response = await client.GetAsync(TransactionsUri(portfolioId, bond.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.ReadJsonAsync(cancellationToken)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var credit = Assert.Single(items, i => i.GetProperty("quantity").GetDecimal() == 267.50m);
        Assert.Equal(1, credit.GetProperty("bondInterestPeriodIndex").GetInt32());
        var opening = Assert.Single(items, i => i.GetProperty("quantity").GetDecimal() == 5_000m);
        Assert.True(!opening.TryGetProperty("bondInterestPeriodIndex", out var value) || value.ValueKind == JsonValueKind.Null);
    }
}
