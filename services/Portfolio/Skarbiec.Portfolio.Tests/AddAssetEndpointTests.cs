using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class AddAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Theory]
    [InlineData(AssetClass.Cash)]
    [InlineData(AssetClass.Deposit)]
    [InlineData(AssetClass.Stock)]
    [InlineData(AssetClass.Etf)]
    [InlineData(AssetClass.Bond)]
    [InlineData(AssetClass.Crypto)]
    [InlineData(AssetClass.PreciousMetal)]
    [InlineData(AssetClass.RealEstate)]
    [InlineData(AssetClass.Other)]
    public async Task Add_EveryAssetClassWithManualValue_ReturnsCreated(AssetClass assetClass)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = assetClass,
            Name = "Test asset",
            Currency = "USD",
            Quantity = 2.5m,
            ManualValue = 1000.50m,
            ManualValueDate = new DateOnly(2026, 7, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(assetClass, body.AssetClass);
        Assert.Equal("USD", body.Currency);
        Assert.Equal(2.5m, body.Quantity);
        Assert.Equal(1000.50m, body.ManualValue);
        Assert.Equal(new DateOnly(2026, 7, 1), body.ManualValueDate);
        Assert.Equal(AssetUri(portfolioId, body.Id), response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Add_WithoutQuantity_DefaultsToZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new { AssetClass = AssetClass.RealEstate, Name = "Flat", ManualValue = 500000m, ManualValueDate = new DateOnly(2026, 1, 1) };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(0m, body!.Quantity);
        Assert.Equal("PLN", body.Currency);
    }

    [Fact]
    public async Task Add_WithNegativeManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Negative value",
            Currency = "PLN",
            ManualValue = -1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithNegativeQuantity_ReturnsBadRequestWithFieldDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Negative quantity",
            Currency = "PLN",
            Quantity = -5m,
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Contains(problem.Errors, e => e.Key.Equals(nameof(AddAssetRequest.Quantity), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Add_ForNonExistentPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Orphan",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(Guid.NewGuid()), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Cash,
            Name = "Unauthorized",
            Currency = "PLN",
            ManualValue = 1m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(Guid.NewGuid()), request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithValidInstrument_ReturnsCreatedAsMarketAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Apple",
            Currency = "USD",
            Quantity = 10m,
            InstrumentId = instrumentId
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(instrumentId, body.InstrumentId);
        Assert.Null(body.ManualValue);
        Assert.Null(body.ManualValueDate);
    }

    [Fact]
    public async Task Add_WithNonExistentInstrument_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithNotFound(instrumentId);
        var request = new AddAssetRequest { AssetClass = AssetClass.Stock, Name = "Ghost", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_WhenMarketDataUnavailable_ReturnsServiceUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var instrumentId = Guid.NewGuid();
        Factory.InstrumentLookupClient.WithUnavailable(instrumentId);
        var request = new AddAssetRequest { AssetClass = AssetClass.Stock, Name = "Down", Currency = "USD", InstrumentId = instrumentId };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithBothInstrumentIdAndManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Ambiguous",
            Currency = "USD",
            InstrumentId = Guid.NewGuid(),
            ManualValue = 100m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithNeitherInstrumentIdNorManualValue_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        var request = new AddAssetRequest { AssetClass = AssetClass.Stock, Name = "Neither", Currency = "USD" };

        var response = await client.PostAsJsonAsync(AssetsUri(portfolioId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_AssetToPortfolio_MakesPortfolioDeleteConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await client.CreatePortfolioAsync(cancellationToken);
        await client.AddAssetAsync(portfolioId, cancellationToken, name: "Blocks portfolio delete", assetClass: AssetClass.Cash, manualValue: 1m);

        var deleteResponse = await client.DeleteAsync(PortfolioUri(portfolioId), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, deleteResponse.StatusCode);
    }
}
