using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
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
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken, currency: "EUR");
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 10m,
            UnitPrice = 100m,
            Date = new DateOnly(2026, 3, 4)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.ReadJsonAsync(cancellationToken);
        json.AssertCarriesNoFee();
        var body = json.Deserialize<TransactionResponse>(JsonSerializerOptions.Web);
        Assert.NotNull(body);
        Assert.Equal(TransactionType.Buy, body.Type);
        Assert.Equal(10m, body.Quantity);
        Assert.Equal(100m, body.UnitPrice);
        Assert.Equal("EUR", body.Currency);
        Assert.Equal(4300.00m, body.ValuePln);
        Assert.Equal(TransactionUri(portfolioId, assetId, body.Id), response.Headers.Location?.OriginalString);
        Assert.Equal(("EUR", new DateOnly(2026, 3, 4)), Assert.Single(Factory.FxRateLookupClient.Calls));

        Assert.Equal(10m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Record_PlnAsset_ValuePlnEqualsAmountWithoutFxLookup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken, currency: "PLN");
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Deposit,
            Quantity = 1000m,
            UnitPrice = 1m,
            Date = new DateOnly(2026, 3, 4)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);

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
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken, currency: "EUR");
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 2m,
            UnitPrice = 50m,
            Date = new DateOnly(2020, 1, 2)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("EUR", body.Currency);
        Assert.Null(body.ValuePln);
        Assert.Single(Factory.FxRateLookupClient.Calls);

        var listed = Assert.Single((await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items);
        Assert.Null(listed.ValuePln);
        Assert.Equal(2m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Record_WhenMarketDataUnavailable_ReturnsServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithUnavailable("EUR");
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken, currency: "EUR");
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 3m,
            UnitPrice = 10m,
            Date = new DateOnly(2026, 3, 4)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Single(Factory.FxRateLookupClient.Calls);
        Assert.Equal(0, (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).TotalCount);
        var asset = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(0m, asset.Quantity);
        Assert.Equal(0, asset.TransactionCount);
    }

    [Fact]
    public async Task Record_ValuePln_RoundsMidpointAwayFromZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken, currency: "EUR");
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 1m,
            UnitPrice = 0.35m,
            Date = new DateOnly(2026, 3, 4)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        Assert.Equal(1.51m, body!.ValuePln);
    }

    [Fact]
    public async Task Record_OnArchivedPortfolio_ReturnsConflictWithoutFxLookup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.FxRateLookupClient.WithRate("EUR", 4.30m);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken, currency: "EUR");
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 3m,
            UnitPrice = 10m,
            Date = new DateOnly(2026, 3, 4)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        Assert.Empty(Factory.FxRateLookupClient.Calls);
        Assert.Equal(0, (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Record_SellMoreThanPosition_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 1), cancellationToken);
        var oversell = new RecordTransactionRequest
        {
            Type = TransactionType.Sell,
            Quantity = 6m,
            UnitPrice = 100m,
            Date = new DateOnly(2026, 1, 2)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), oversell, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.Equal(5m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Record_NegativeQuantity_ReturnsBadRequestWithFieldDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = -1m,
            UnitPrice = 10m,
            Date = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);

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
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, Guid.NewGuid()), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Record_AssetUnderWrongPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var otherPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Other portfolio");
        var request = new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(otherPortfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Record_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();
        var request = new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(Guid.NewGuid(), Guid.NewGuid()), request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Record_BuySellDividendSequence_YieldsCorrectQuantityAndHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);

        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Sell, 4m, new DateOnly(2026, 1, 5), cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Dividend, 0m, new DateOnly(2026, 1, 10), cancellationToken);

        Assert.Equal(6m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);

        var page = await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken);
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
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddCashAssetAsync(portfolioId, cancellationToken, currency: "EUR");
        await client.RecordTransactionAsync(
            portfolioId, assetId, TransactionType.Deposit, 1_000m, new DateOnly(2026, 1, 1), cancellationToken, unitPrice: 1m);
        var fxCallsBefore = Factory.FxRateLookupClient.Calls.Count;
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 10m,
            UnitPrice = 1m,
            Date = new DateOnly(2026, 1, 2)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertTransactionTypeNotAllowedAsync(cancellationToken);
        Assert.Equal(fxCallsBefore, Factory.FxRateLookupClient.Calls.Count);
        var asset = await client.GetAssetAsync(portfolioId, assetId, cancellationToken);
        Assert.Equal(1_000m, asset.Quantity);
        Assert.Equal(1, asset.TransactionCount);
        var listed = Assert.Single((await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).Items);
        Assert.Equal(TransactionType.Deposit, listed.Type);
    }

    [Fact]
    public async Task Record_DepositAndWithdrawOnCashAsset_Succeed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var assetId = await client.AddCashAssetAsync(portfolioId, cancellationToken);
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

        var depositResponse = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), deposit, cancellationToken);
        var withdrawResponse = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), withdraw, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, depositResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, withdrawResponse.StatusCode);
        Assert.Equal(800m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
    }

    [Fact]
    public async Task Record_ArchivedAsset_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var cashId = await client.AddCashAssetWithBalanceAsync(portfolioId, cancellationToken, balance: 100m);
        await client.ArchiveAssetAsync(portfolioId, cashId, cancellationToken);
        var request = new RecordTransactionRequest { Type = TransactionType.Deposit, Quantity = 50m, UnitPrice = 1m, Date = new DateOnly(2026, 2, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, cashId), request, cancellationToken);

        await response.AssertAssetArchivedConflictAsync(cancellationToken);
        Assert.Equal(100m, (await client.GetAssetAsync(portfolioId, cashId, cancellationToken)).Quantity);
        Assert.Equal(1, (await client.ListTransactionsAsync(portfolioId, cashId, cancellationToken)).TotalCount);
    }

    [Fact]
    public async Task Record_InArchivedPortfolio_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.ArchivePortfolioAsync(portfolioId, cancellationToken);
        var request = new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 3m, UnitPrice = 10m, Date = new DateOnly(2026, 1, 2) };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);

        await response.AssertPortfolioArchivedConflictAsync(cancellationToken);
        Assert.Equal(5m, (await client.GetAssetAsync(portfolioId, assetId, cancellationToken)).Quantity);
        Assert.Equal(1, (await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken)).TotalCount);
    }

    [Theory]
    [InlineData(TransactionType.Deposit)]
    [InlineData(TransactionType.Withdraw)]
    public async Task Record_OnTermDeposit_ReturnsConflict(TransactionType type)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, deposit) = await client.CreatePortfolioWithDepositAsync(cancellationToken);
        var request = new RecordTransactionRequest { Type = type, Quantity = 500m, UnitPrice = 1m, Date = new DateOnly(2026, 2, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, deposit.AssetId), request, cancellationToken);

        await response.AssertDepositTransactionsManagedAsync(cancellationToken);
        Assert.Equal(10_000m, (await client.GetAssetAsync(portfolioId, deposit.AssetId, cancellationToken)).Quantity);
        Assert.Equal(1, (await client.ListTransactionsAsync(portfolioId, deposit.AssetId, cancellationToken)).TotalCount);
    }

    [Theory]
    [InlineData(TransactionType.Deposit, true)]
    [InlineData(TransactionType.Withdraw, true)]
    [InlineData(TransactionType.Buy, false)]
    [InlineData(TransactionType.Sell, false)]
    [InlineData(TransactionType.Dividend, false)]
    [InlineData(TransactionType.Interest, false)]
    public async Task Record_OnSavingsAccount_AcceptsDepositAndWithdrawOnly(TransactionType type, bool accepted)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        var request = new RecordTransactionRequest { Type = type, Quantity = 100m, UnitPrice = 1m, Date = SavingsToday };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, account.AssetId), request, cancellationToken);

        var asset = await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken);
        if (accepted)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(type == TransactionType.Deposit ? 10_100m : 9_900m, asset.Quantity);
            Assert.Equal(2, asset.TransactionCount);
            Assert.Equal(asset.Quantity, (await client.GetSavingsAccountAsync(portfolioId, account.AssetId, cancellationToken)).Balance);
        }
        else
        {
            await response.AssertTransactionTypeNotAllowedAsync(cancellationToken);
            Assert.Equal(10_000m, asset.Quantity);
            Assert.Equal(1, asset.TransactionCount);
        }
    }

    [Fact]
    public async Task Record_OnSavingsAccount_WithdrawBeyondBalance_ReturnsOversell()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, account) = await client.CreatePortfolioWithSavingsAccountAsync(cancellationToken);
        var request = new RecordTransactionRequest { Type = TransactionType.Withdraw, Quantity = 10_000.01m, UnitPrice = 1m, Date = SavingsToday };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, account.AssetId), request, cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, "Validation.OversellsPosition", cancellationToken);
        Assert.Equal(10_000m, (await client.GetAssetAsync(portfolioId, account.AssetId, cancellationToken)).Quantity);
    }
}
