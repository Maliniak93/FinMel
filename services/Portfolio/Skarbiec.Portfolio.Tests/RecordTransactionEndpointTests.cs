using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class RecordTransactionEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Record_Buy_ReturnsCreatedAndUpdatesAssetQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken, currency: "EUR");
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 10m,
            UnitPrice = 100m,
            Date = new DateOnly(2026, 3, 4)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.ReadJsonAsync(cancellationToken);
        var body = json.Deserialize<TransactionResponse>(JsonSerializerOptions.Web);
        Assert.NotNull(body);
        Assert.Null(body.FeeAmount);
        Assert.Equal(TransactionType.Buy, body.Type);
        Assert.Equal(10m, body.Quantity);
        Assert.Equal(100m, body.UnitPrice);
        Assert.Equal("EUR", body.Currency);
        Assert.Equal(4300.00m, body.ValuePln);
        Assert.Equal(TransactionUri(assetId, body.Id), response.Headers.Location?.OriginalString);
        Assert.Equal(("EUR", new DateOnly(2026, 3, 4)), Assert.Single(Factory.FxRateLookupClient.Calls));

        Assert.Equal(10m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Record_PlnAsset_ValuePlnEqualsAmountWithoutFxLookup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken, currency: "PLN");
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 10m,
            UnitPrice = 100m,
            Date = new DateOnly(2026, 3, 4)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("PLN", body.Currency);
        Assert.Equal(1000.00m, body.ValuePln);
        Assert.Empty(Factory.FxRateLookupClient.Calls);
    }

    [Fact]
    public async Task Record_WhenNoRateForDate_SavesWithNullValuePln()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithNotFound("EUR");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var assetId = await client.AddCashAssetAsync(cancellationToken, currency: "EUR");
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Deposit,
            Quantity = 100m,
            UnitPrice = 1m,
            Date = new DateOnly(2020, 1, 2)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("EUR", body.Currency);
        Assert.Null(body.ValuePln);
        Assert.Single(Factory.FxRateLookupClient.Calls);

        var listed = Assert.Single((await client.ListTransactionsAsync(assetId, cancellationToken)).Items);
        Assert.Null(listed.ValuePln);
        Assert.Equal(100m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Record_WhenMarketDataUnavailable_ReturnsServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithUnavailable("EUR");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken, currency: "EUR");
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 3m,
            UnitPrice = 10m,
            Date = new DateOnly(2026, 3, 4)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(Factory.FxRateLookupClient.Calls);
        Assert.Equal(0, (await client.ListTransactionsAsync(assetId, cancellationToken)).TotalCount);
        var asset = await client.GetAssetAsync(assetId, cancellationToken);
        Assert.Equal(0m, asset.Quantity);
        Assert.Equal(0, asset.TransactionCount);
    }

    [Fact]
    public async Task Record_ValuePln_RoundsMidpointAwayFromZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken, currency: "EUR");
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 1m,
            UnitPrice = 0.35m,
            Date = new DateOnly(2026, 3, 4)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        Assert.Equal(1.51m, body!.ValuePln);
    }

    [Fact]
    public async Task Record_OnArchivedAccount_ReturnsConflictWithoutFxLookup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken, currency: "EUR");
        await client.ArchiveAccountAsync(accountId, cancellationToken);
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 3m,
            UnitPrice = 10m,
            Date = new DateOnly(2026, 3, 4)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), request, cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
        Assert.Empty(Factory.FxRateLookupClient.Calls);
        Assert.Equal(0, (await client.ListTransactionsAsync(assetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Record_SellMoreThanPosition_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 1), cancellationToken);
        var oversell = new RecordTransactionRequest
        {
            Type = TransactionType.Sell,
            Quantity = 6m,
            UnitPrice = 100m,
            Date = new DateOnly(2026, 1, 2)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), oversell, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Equal(5m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Record_NegativeQuantity_ReturnsBadRequestWithFieldDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = -1m,
            UnitPrice = 10m,
            Date = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors, e => e.Key.Equals(nameof(RecordTransactionRequest.Quantity), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Record_ForNonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(Guid.NewGuid()), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Record_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();
        var request = new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(Guid.NewGuid()), request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Record_BuySellDividendSequence_YieldsCorrectQuantityAndHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);

        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Sell, 4m, new DateOnly(2026, 1, 5), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Dividend, 0m, new DateOnly(2026, 1, 10), cancellationToken);

        Assert.Equal(6m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);

        var page = await client.ListTransactionsAsync(assetId, cancellationToken);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(
            [TransactionType.Dividend, TransactionType.Sell, TransactionType.Buy],
            page.Items.Select(t => t.Type));
    }

    [Fact]
    public async Task Record_DisallowedTypeOnCashAsset_ReturnsBadRequestAndWritesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var assetId = await client.AddCashAssetAsync(cancellationToken, currency: "EUR");
        await client.RecordTransactionAsync(
            assetId, TransactionType.Deposit, 1_000m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
        var fxCallsBefore = Factory.FxRateLookupClient.Calls.Count;
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 10m,
            UnitPrice = 1m,
            Date = new DateOnly(2026, 1, 2)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), request, cancellationToken);

        await response.AssertTransactionTypeNotAllowedAsync(cancellationToken);
        Assert.Equal(fxCallsBefore, Factory.FxRateLookupClient.Calls.Count);
        var asset = await client.GetAssetAsync(assetId, cancellationToken);
        Assert.Equal(1_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
        var listed = Assert.Single((await client.ListTransactionsAsync(assetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, listed.Type);
    }

    [Fact]
    public async Task Record_DepositAndWithdrawOnCashAsset_Succeed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var assetId = await client.AddCashAssetAsync(cancellationToken);
        var deposit = new RecordTransactionRequest
        {
            Type = TransactionType.Deposit,
            Quantity = 1_000m,
            UnitPrice = 1m,
            Date = new DateOnly(2026, 1, 1)
        };
        var withdraw = new RecordTransactionRequest
        {
            Type = TransactionType.Withdraw,
            Quantity = 200m,
            UnitPrice = 1m,
            Date = new DateOnly(2026, 1, 2)
        };

        var depositResponse = await client.PostAsJsonAsync(TransactionsUri(assetId), deposit, cancellationToken);
        var withdrawResponse = await client.PostAsJsonAsync(TransactionsUri(assetId), withdraw, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, depositResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, withdrawResponse.StatusCode);
        Assert.Equal(800m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Record_ArchivedAsset_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 100m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.ArchiveAssetAsync(assetId, cancellationToken);
        var request = new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 50m, UnitPrice = 10m, Date = new DateOnly(2026, 2, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), request, cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        Assert.Equal(100m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        Assert.Equal(1, (await client.ListTransactionsAsync(assetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Record_InArchivedAccount_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Sell, 5m, new DateOnly(2026, 1, 2), cancellationToken);
        await client.ArchiveAccountAsync(accountId, cancellationToken);
        var request = new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 3m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 3) };

        var response = await client.PostAsJsonAsync(TransactionsUri(assetId), request, cancellationToken);

        await response.AssertAccountArchivedConflictAsync(cancellationToken);
        Assert.Equal(0m, (await client.GetAssetAsync(assetId, cancellationToken)).Quantity);
        Assert.Equal(2, (await client.ListTransactionsAsync(assetId, cancellationToken)).TotalCount);
    }

    [Theory]
    [InlineData(TransactionType.Deposit)]
    [InlineData(TransactionType.Withdraw)]
    public async Task Record_OnTermDeposit_ReturnsConflict(TransactionType type)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        var request = new RecordTransactionRequest { Type = type, Quantity = 500m, UnitPrice = 1m, Date = new DateOnly(2026, 2, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(deposit.AssetId), request, cancellationToken);

        await response.AssertDepositTransactionsManagedAsync(cancellationToken);
        Assert.Equal(10_000m, (await client.GetAssetAsync(deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(1, (await client.ListTransactionsAsync(deposit.AssetId, cancellationToken)).TotalCount);
    }

    [Theory]
    [InlineData(TransactionType.Deposit, true)]
    [InlineData(TransactionType.Withdraw, true)]
    [InlineData(TransactionType.Buy, false)]
    public async Task Record_OnBankAccount_AcceptsDepositAndWithdrawOnly(TransactionType type, bool accepted)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateSavingsBankAccountAsync(cancellationToken);
        var request = new RecordTransactionRequest { Type = type, Quantity = 100m, UnitPrice = 1m, Date = FixedToday };

        var response = await client.PostAsJsonAsync(TransactionsUri(account.MoneyAssetId), request, cancellationToken);

        var asset = await client.GetAssetAsync(account.MoneyAssetId, cancellationToken);
        if (accepted)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(type == TransactionType.Deposit ? 10_100m : 9_900m, asset.Quantity);
            Assert.Equal(2, asset.TransactionCount);
            Assert.Equal(asset.Quantity, await client.GetCashBalanceAsync(account.AccountId, cancellationToken));
        }
        else
        {
            await response.AssertTransactionTypeNotAllowedAsync(cancellationToken);
            Assert.Equal(10_000m, asset.Quantity);
            Assert.Equal(1, asset.TransactionCount);
        }
    }

    [Fact]
    public async Task Record_OnBankAccount_WithdrawBeyondBalance_ReturnsInsufficientFunds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(FixedTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateSavingsBankAccountAsync(cancellationToken);
        var request = new RecordTransactionRequest { Type = TransactionType.Withdraw, Quantity = 10_000.01m, UnitPrice = 1m, Date = FixedToday };

        var response = await client.PostAsJsonAsync(TransactionsUri(account.MoneyAssetId), request, cancellationToken);

        await response.AssertInsufficientFundsAsync(cancellationToken);
        Assert.Equal(10_000m, (await client.GetAssetAsync(account.MoneyAssetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Record_WithAmountPln_StoresAmountAndEffectiveRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.FxRateLookupClient.WithUnavailable("EUR");
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(cancellationToken, currency: "EUR");
        var request = NewBrokerageTradeRequest(
            quantity: 10m, unitPrice: 100m, date: CashDay1, feeAmount: 5m, amountPln: 4_312.50m);

        var response = await client.PostAsJsonAsync(TransactionsUri(holding.AssetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken))!;
        Assert.Equal(4_312.50m, body.AmountPln);
        Assert.Equal(4_312.50m, body.ValuePln);
        await using var dbContext = CreateDbContext(userId);
        var stored = await dbContext.Transactions.SingleAsync(t => t.Id == body.Id, cancellationToken);
        Assert.Equal(4_312.50m, stored.AmountPln);
        Assert.Equal(4.3125m, stored.FxRateToPln);
        Assert.Equal(682.50m, await client.GetCashBalanceAsync(holding.AccountId, cancellationToken));
        Assert.DoesNotContain(Factory.FxRateLookupClient.Calls, call => call.Currency == "EUR");
    }

    [Theory]
    [InlineData("PlnAsset")]
    [InlineData("Dividend")]
    [InlineData("NonPositiveAmount")]
    [InlineData("ZeroUnitPrice")]
    public async Task Record_AmountPlnNotAllowed_Returns400(string scenario)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AccountCashTodayUtc);
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var holding = await client.CreateBrokerageHoldingAsync(
            cancellationToken, currency: scenario == "PlnAsset" ? "PLN" : "EUR");
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);
        var amountPln = scenario == "NonPositiveAmount" ? 0m : 1_000m;
        var request = scenario switch
        {
            "Dividend" => NewBrokerageTradeRequest(TransactionType.Dividend, 40m, 1m, CashDay1, amountPln: amountPln),
            "ZeroUnitPrice" => NewBrokerageTradeRequest(quantity: 10m, unitPrice: 0m, date: CashDay1, amountPln: amountPln),
            _ => NewBrokerageTradeRequest(quantity: 10m, unitPrice: 100m, date: CashDay1, amountPln: amountPln)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(holding.AssetId), request, cancellationToken);

        await response.AssertProblemAsync(
            HttpStatusCode.BadRequest, PortfolioAssertions.PlnAmountNotAllowedErrorCode, cancellationToken);
        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
        Assert.Equal(5_000m, await client.GetCashBalanceAsync(holding.AccountId, cancellationToken));
    }
}
