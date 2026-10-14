using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.Transfers;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ListTransactionsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ReturnsOnlyTransactionsOfThatAsset_NewestFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.FundAccountAsync(accountId, cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken, name: "Asset A");
        var otherAssetId = await client.AddAssetAsync(accountId, cancellationToken, name: "Asset B");

        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 10), cancellationToken);
        await client.RecordTransactionAsync(otherAssetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 15), cancellationToken);

        var response = await client.GetAsync(TransactionsUri(assetId), cancellationToken);

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
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.FundAccountAsync(accountId, cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken, name: "Asset A");

        for (var day = 1; day <= 5; day++)
        {
            await client.RecordTransactionAsync(assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, day), cancellationToken);
        }

        var response = await client.GetAsync($"{TransactionsUri(assetId)}?page=2&pageSize=2", cancellationToken);

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
        var assetId = await client.AddCashAssetAsync(cancellationToken, currency: "EUR");
        await client.RecordTransactionAsync(assetId, TransactionType.Deposit, 100m, pricedDate, cancellationToken, unitPrice: 1m);
        Factory.FxRateLookupClient.WithNotFound("EUR");
        await client.RecordTransactionAsync(assetId, TransactionType.Deposit, 50m, unpricedDate, cancellationToken, unitPrice: 1m);

        var response = await client.GetAsync(TransactionsUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.ReadJsonAsync(cancellationToken)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal("EUR", item.GetProperty("currency").GetString()));
        var priced = items[0].Deserialize<TransactionResponse>(JsonSerializerOptions.Web)!;
        var unpriced = items[1].Deserialize<TransactionResponse>(JsonSerializerOptions.Web)!;
        Assert.Equal(pricedDate, priced.Date);
        Assert.Equal(430.00m, priced.ValuePln);
        Assert.Null(priced.FeeAmount);
        Assert.Equal(unpricedDate, unpriced.Date);
        Assert.Null(unpriced.ValuePln);
    }

    [Fact]
    public async Task List_ReturnsAmountPlnAsValuePln()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.FxRateLookupClient.WithRate("EUR", 4.00m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken, currency: "EUR");
        await client.RecordBrokerageTradeAsync(
            holding.AssetId, cancellationToken, quantity: 10m, unitPrice: 100m, date: CashDay1, amountPln: 4_312.50m);

        var items = (await client.ListTransactionsAsync(holding.AssetId, cancellationToken)).Items;

        var buy = Assert.Single(items);
        Assert.Equal(4_312.50m, buy.AmountPln);
        Assert.Equal(4_312.50m, buy.ValuePln);
    }

    [Fact]
    public async Task List_TransferLeg_ReturnsCounterpart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateFundedDepositAsync(cancellationToken);

        var cashResponse = await client.GetAsync(TransactionsUri(funded.CashAssetId), cancellationToken);
        var depositResponse = await client.GetAsync(TransactionsUri(funded.Deposit.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, cashResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, depositResponse.StatusCode);
        var cashItems = (await cashResponse.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(cancellationToken))!.Items;
        var depositItems = (await depositResponse.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(cancellationToken))!.Items;

        var outLeg = Assert.Single(cashItems, t => t.Type == TransactionType.Withdraw);
        Assert.NotNull(outLeg.Transfer);
        Assert.Equal(funded.Deposit.AssetId, outLeg.Transfer.CounterpartAssetId);
        Assert.Equal("Term deposit", outLeg.Transfer.CounterpartAssetName);
        Assert.Null(outLeg.Transfer.CounterpartAccountName);
        Assert.Equal(TransferDirection.Out, outLeg.Transfer.Direction);

        var plain = Assert.Single(cashItems, t => t.Type == TransactionType.Deposit);
        Assert.Null(plain.Transfer);

        var inLeg = Assert.Single(depositItems);
        Assert.NotNull(inLeg.Transfer);
        Assert.Equal(funded.CashAssetId, inLeg.Transfer.CounterpartAssetId);
        Assert.Equal("Cash account", inLeg.Transfer.CounterpartAssetName);
        Assert.Equal(TransferDirection.In, inLeg.Transfer.Direction);
    }

    [Fact]
    public async Task List_ForNonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(TransactionsUri(Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(TransactionsUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task List_BankInterestCredit_ReturnsPeriodEnd()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SeptemberEndedUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateInterestAccountAsync(cancellationToken);
        await client.SettlePreviewedInterestAsync(account.AccountId, cancellationToken);

        var response = await client.GetAsync(TransactionsUri(account.MoneyAssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.ReadJsonAsync(cancellationToken)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var credit = Assert.Single(items, i => i.GetProperty("quantity").GetDecimal() == 33.29m);
        Assert.Equal("2026-09-30", credit.GetProperty("bankInterestPeriodEnd").GetString());
        var opening = Assert.Single(items, i => i.GetProperty("quantity").GetDecimal() == 10_000m);
        Assert.True(!opening.TryGetProperty("bankInterestPeriodEnd", out var value) || value.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task List_ManualTransferLeg_IsManual()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateCurrentAndSavingsAsync(cancellationToken);
        var transferId = await client.CreateTransferAsync(cancellationToken, NewTransferRequest(setup.Current.MoneyAssetId, setup.Savings.MoneyAssetId, 2_000m));

        var cashLeg = await client.GetCashWithdrawAsync(setup.Current.MoneyAssetId, cancellationToken);
        var bankLeg = Assert.Single((await client.ListTransactionsAsync(setup.Savings.MoneyAssetId, cancellationToken)).Items);

        Assert.NotNull(cashLeg.Transfer);
        Assert.Equal(transferId, cashLeg.Transfer.TransferId);
        Assert.True(cashLeg.Transfer.Manual);
        Assert.NotNull(bankLeg.Transfer);
        Assert.Equal(transferId, bankLeg.Transfer.TransferId);
        Assert.True(bankLeg.Transfer.Manual);
        Assert.Equal(TransferDirection.In, bankLeg.Transfer.Direction);

        using var fundingClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await fundingClient.CreateFundedDepositAsync(cancellationToken);
        var fundingLeg = await fundingClient.GetCashWithdrawAsync(funded.CashAssetId, cancellationToken);
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
        var buy = await client.RecordMetalTransactionWithCashAsync(setup.MetalAssetId, setup.CashAssetId, cancellationToken);
        var sell = await client.RecordMetalTransactionWithCashAsync(
            setup.MetalAssetId, setup.CashAssetId, cancellationToken,
            type: TransactionType.Sell, pieces: 1m, pricePerPiece: 1_300m, date: MetalCashDate.AddDays(1));

        var cashLegs = (await client.ListTransactionsAsync(setup.CashAssetId, cancellationToken)).Items;
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
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken);
        await client.RecordBrokerageTradeAsync(holding.AssetId, cancellationToken);

        var buy = Assert.Single((await client.ListTransactionsAsync(holding.AssetId, cancellationToken)).Items);
        var withdraw = Assert.Single(
            (await client.ListTransactionsAsync(holding.CashAssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);

        Assert.NotNull(buy.Transfer);
        Assert.Equal(holding.CashAssetId, buy.Transfer.CounterpartAssetId);
        Assert.Equal("XTB IKE", buy.Transfer.CounterpartAccountName);
        Assert.Equal(TransferDirection.In, buy.Transfer.Direction);
        Assert.False(buy.Transfer.Manual);
        Assert.NotNull(withdraw.Transfer);
        Assert.Equal(buy.Transfer.TransferId, withdraw.Transfer.TransferId);
        Assert.Equal(holding.AssetId, withdraw.Transfer.CounterpartAssetId);
        Assert.Equal("XTB IKE", withdraw.Transfer.CounterpartAccountName);
        Assert.Equal(TransferDirection.Out, withdraw.Transfer.Direction);
        Assert.False(withdraw.Transfer.Manual);
    }

    [Fact]
    public async Task List_DepositPayoutLegOnBankAccount_IsNotManual()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var setup = await client.CreateDepositPaidIntoBankAsync(cancellationToken);

        var bankLeg = Assert.Single((await client.ListTransactionsAsync(setup.Account.MoneyAssetId, cancellationToken)).Items);
        var depositLeg = Assert.Single(
            (await client.ListTransactionsAsync(setup.Deposit.AssetId, cancellationToken)).Items,
            t => t.Type == TransactionType.Withdraw);

        Assert.NotNull(bankLeg.Transfer);
        Assert.False(bankLeg.Transfer.Manual);
        Assert.Equal(TransferDirection.In, bankLeg.Transfer.Direction);
        Assert.NotNull(depositLeg.Transfer);
        Assert.False(depositLeg.Transfer.Manual);
        Assert.Equal(bankLeg.Transfer.TransferId, depositLeg.Transfer.TransferId);
    }

    [Fact]
    public async Task List_BondInterestCredit_CarriesPeriod()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(new DateTimeOffset(2027, 10, 2, 10, 0, 0, TimeSpan.Zero));
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, bond) = await client.CreateAccountWithBondAsync(cancellationToken);
        await client.SettleBondInterestAsync(bond.AssetId, [(1, null)], cancellationToken);

        var response = await client.GetAsync(TransactionsUri(bond.AssetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.ReadJsonAsync(cancellationToken)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        var credit = Assert.Single(items, i => i.GetProperty("quantity").GetDecimal() == 267.50m);
        Assert.Equal(1, credit.GetProperty("bondInterestPeriodIndex").GetInt32());
        var opening = Assert.Single(items, i => i.GetProperty("quantity").GetDecimal() == 5_000m);
        Assert.True(!opening.TryGetProperty("bondInterestPeriodIndex", out var value) || value.ValueKind == JsonValueKind.Null);
    }
}
