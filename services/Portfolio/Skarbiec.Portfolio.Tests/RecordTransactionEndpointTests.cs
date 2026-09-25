using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class RecordTransactionEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>transactions-pln-value-and-fee-removal AC3: an EUR Buy of 10 @ 100 at a fake rate of
    /// 4.30 answers with the asset's currency, <c>valuePln 4300.00</c> and no <c>fee</c>; the rate
    /// was looked up for the transaction's own date.</summary>
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

    /// <summary>transactions-pln-value-and-fee-removal AC4: a PLN asset is valued at rate 1 and never
    /// asks MarketData.</summary>
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

    /// <summary>transactions-pln-value-and-fee-removal AC5: no rate on or before the date still saves
    /// the transaction — its PLN value is simply unknown (<c>null</c>), in the response and the list.</summary>
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

    /// <summary>transactions-pln-value-and-fee-removal AC6: MarketData being down is a transient
    /// failure, not a missing rate — 503 <c>ServiceUnavailable.MarketData</c>, and nothing is saved.</summary>
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

    /// <summary>transactions-pln-value-and-fee-removal: <c>ValuePln</c> is rounded to 2 places,
    /// midpoint away from zero — 1 × 0.35 × 4.30 = 1.505 → 1.51 (banker's rounding would give 1.50).</summary>
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

    /// <summary>transactions-pln-value-and-fee-removal AC10: the archived guard runs before the rate
    /// lookup — a write to an archived portfolio is a 409 and never calls MarketData.</summary>
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

    /// <summary>cash-transaction-types AC-2: a Cash asset accepts only Deposit/Withdraw — a Buy is a
    /// 400 <c>Validation.TransactionTypeNotAllowed</c>, checked before the FX lookup and the
    /// recompute, so the quantity and the transaction list stay as they were. (The "no outbox row"
    /// half is proven hostless in <see cref="PortfolioOutboxTests"/>, where no bus can deliver and
    /// delete the row before the assertion.)</summary>
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

    /// <summary>cash-transaction-types AC-2: the two types a Cash asset does accept still record —
    /// a Deposit and then a Withdraw both answer 201 and move the balance.</summary>
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

    /// <summary>archived-portfolio-out-of-net-worth AC7: recording a transaction on an asset of an
    /// archived portfolio is a 409 <c>Conflict.PortfolioArchived</c> and the quantity is unchanged.</summary>
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
}
