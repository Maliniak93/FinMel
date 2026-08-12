using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.UpdateAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class UpdateAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    /// <summary>
    /// M1.5: <c>UpdateAssetRequest</c> has no directly-settable <c>Quantity</c> (see its
    /// <c>&lt;remarks&gt;</c>) — a prior Buy transaction gives the asset a real, non-zero quantity, and
    /// the update (which touches every other field) must leave it exactly as the transaction produced
    /// it, proving Quantity moves only through <c>TransactionQuantityCalculator</c> (ADR-009), never
    /// through this endpoint.
    /// </summary>
    [Fact]
    public async Task Update_ExistingAsset_ReturnsOkWithUpdatedFieldsAndQuantityUntouched()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 3m, new DateOnly(2026, 1, 1), cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Crypto,
            Name = "Renamed",
            Currency = "USD",
            ManualValue = 42.42m,
            ManualValueDate = new DateOnly(2026, 6, 15)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(AssetClass.Crypto, body!.AssetClass);
        Assert.Equal("Renamed", body.Name);
        Assert.Equal("USD", body.Currency);
        Assert.Equal(3m, body.Quantity);
        Assert.Equal(42.42m, body.ManualValue);
        Assert.Equal(new DateOnly(2026, 6, 15), body.ManualValueDate);
    }

    [Theory]
    [InlineData("PLN")]
    [InlineData("EUR")]
    [InlineData("USD")]
    public async Task Update_WithSupportedCurrency_ReturnsOk(string currency)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = $"Cash in {currency}",
            Currency = currency,
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(currency, body!.Currency);
    }

    [Fact]
    public async Task Update_WithUnsupportedCurrency_ReturnsBadRequestNamingAcceptedSet()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Swiss cash",
            Currency = "CHF",
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors, e => e.Key.Equals(nameof(UpdateAssetRequest.Currency), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            problem.Errors.Values.SelectMany(messages => messages),
            message => message.Contains(SupportedCurrencies.Accepted, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_WithNegativeManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Still here",
            Currency = "PLN",
            ManualValue = -10m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_NonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Missing",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, Guid.NewGuid()), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_SwitchManualAssetToMarket_NullsManualValueAndKeepsTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 1), cancellationToken);
        var instrumentId = Guid.NewGuid();
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Now market",
            Currency = "USD",
            InstrumentId = instrumentId
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(instrumentId, body!.InstrumentId);
        Assert.Null(body.ManualValue);
        Assert.Null(body.ManualValueDate);
        // M1.5: no Quantity field on the request — the switch to Market must not disturb the
        // quantity the earlier Buy transaction already produced.
        Assert.Equal(5m, body.Quantity);

        var transactions = await client.ListTransactionsAsync(portfolioId, assetId, cancellationToken);
        Assert.Single(transactions.Items);
    }

    [Fact]
    public async Task Update_SwitchMarketAssetBackToManual_ClearsInstrumentId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        await client.PutAsJsonAsync(
            AssetUri(portfolioId, assetId),
            new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Market first", Currency = "USD", InstrumentId = Guid.NewGuid() },
            cancellationToken);

        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Back to manual",
            Currency = "PLN",
            ManualValue = 250m,
            ManualValueDate = new DateOnly(2026, 2, 1)
        };
        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Null(body!.InstrumentId);
        Assert.Equal(250m, body.ManualValue);
    }

    /// <summary>M1.4: switching an existing (manual) asset to currency-valued clears the manual fields
    /// and InstrumentId, and stores the explicit mode.
    /// <para>
    /// M1.5: <c>UpdateAssetRequest</c> has no directly-settable <c>Quantity</c>. A currency-valued
    /// asset's quantity (value = <c>Quantity × FxRate</c>) still has to come from somewhere, and the
    /// answer is the same as every other asset class: recorded transactions (ADR-009) — here a Deposit
    /// of 500 before the mode switch, which the switch must leave untouched.
    /// </para></summary>
    [Fact]
    public async Task Update_SwitchManualCashToCurrencyValued_ClearsManualValueAndSetsModeQuantityFromTransaction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken); // default: manual, AssetClass.Stock.
        await client.RecordTransactionAsync(portfolioId, assetId, TransactionType.Deposit, 500m, new DateOnly(2026, 1, 1), cancellationToken);
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Now currency-valued",
            Currency = "EUR",
        };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(AssetValuationMode.CurrencyValued, body!.ValuationMode);
        Assert.Null(body.ManualValue);
        Assert.Null(body.ManualValueDate);
        Assert.Null(body.InstrumentId);
        Assert.Equal(500m, body.Quantity);
    }

    /// <summary>Mirrors <see cref="AddAssetEndpointTests.Add_WithNeitherInstrumentIdNorManualValue_ReturnsBadRequest"/>
    /// on the update path: "neither" stays a 400 outside the currency-valued classes.</summary>
    [Fact]
    public async Task Update_NonCurrencyValuedClassWithNeitherInstrumentIdNorManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Neither", Currency = "USD" };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithNonExistentInstrument_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithNotFound(instrumentId);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Ghost", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_WhenMarketDataUnavailable_ReturnsServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithUnavailable(instrumentId);
        var request = new UpdateAssetRequest { AssetClass = AssetClass.Stock, Name = "Down", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PutAsJsonAsync(AssetUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Update_AssetUnderWrongPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await client.CreatePortfolioWithAssetAsync(cancellationToken);
        var otherPortfolioId = await client.CreatePortfolioAsync(cancellationToken, name: "Other portfolio");
        var request = new UpdateAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Wrong parent",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PutAsJsonAsync(AssetUri(otherPortfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
