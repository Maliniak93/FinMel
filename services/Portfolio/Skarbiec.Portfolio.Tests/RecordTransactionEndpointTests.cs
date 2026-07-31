using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class RecordTransactionEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Record_Buy_ReturnsCreatedAndUpdatesAssetQuantity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);
        var request = new RecordTransactionRequest
        {
            Type = TransactionType.Buy,
            Quantity = 10m,
            UnitPrice = 100.50m,
            Fee = 5m,
            Date = new DateOnly(2026, 1, 1)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(TransactionType.Buy, body.Type);
        Assert.Equal(10m, body.Quantity);
        Assert.Equal(100.50m, body.UnitPrice);
        Assert.Equal(5m, body.Fee);
        Assert.Equal($"{TransactionsUri(portfolioId, assetId)}/{body.Id}", response.Headers.Location?.OriginalString);

        var assetResponse = await client.GetAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}", cancellationToken);
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(10m, asset!.Quantity);
    }

    [Fact]
    public async Task Record_SellMoreThanPosition_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);
        await RecordAsync(client, portfolioId, assetId, TransactionType.Buy, 5m, new DateOnly(2026, 1, 1), cancellationToken);
        var oversell = new RecordTransactionRequest
        {
            Type = TransactionType.Sell,
            Quantity = 6m,
            UnitPrice = 100m,
            Date = new DateOnly(2026, 1, 2)
        };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), oversell, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var assetResponse = await client.GetAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}", cancellationToken);
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(5m, asset!.Quantity);
    }

    [Fact]
    public async Task Record_NegativeQuantity_ReturnsBadRequestWithFieldDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);
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
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);
        var request = new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, Guid.NewGuid()), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Record_AssetUnderWrongPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (_, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);
        var otherPortfolioId = await CreatePortfolioAsync(client, cancellationToken, "Other portfolio");
        var request = new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(otherPortfolioId, assetId), request, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Record_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();
        var request = new RecordTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PostAsJsonAsync(TransactionsUri(Guid.NewGuid(), Guid.NewGuid()), request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Record_BuySellDividendSequence_YieldsCorrectQuantityAndHistory()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);

        await RecordAsync(client, portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await RecordAsync(client, portfolioId, assetId, TransactionType.Sell, 4m, new DateOnly(2026, 1, 5), cancellationToken);
        await RecordAsync(client, portfolioId, assetId, TransactionType.Dividend, 0m, new DateOnly(2026, 1, 10), cancellationToken);

        var assetResponse = await client.GetAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}", cancellationToken);
        var asset = await assetResponse.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(6m, asset!.Quantity);

        var listResponse = await client.GetAsync(TransactionsUri(portfolioId, assetId), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var page = await listResponse.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(cancellationToken);
        Assert.NotNull(page);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(
            [TransactionType.Dividend, TransactionType.Sell, TransactionType.Buy],
            page.Items.Select(t => t.Type));
    }

    private static string TransactionsUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}/transactions";

    private static async Task RecordAsync(
        HttpClient client, Guid portfolioId, Guid assetId, TransactionType type, decimal quantity, DateOnly date, CancellationToken cancellationToken)
    {
        var request = new RecordTransactionRequest { Type = type, Quantity = quantity, UnitPrice = 10m, Date = date };
        var response = await client.PostAsJsonAsync(TransactionsUri(portfolioId, assetId), request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<Guid> CreatePortfolioAsync(HttpClient client, CancellationToken cancellationToken, string name = "Retirement")
    {
        var response = await client.PostAsJsonAsync(
            PortfoliosUri, new CreatePortfolioRequest { Name = name, Currency = "PLN" }, cancellationToken);
        var portfolio = await response.Content.ReadFromJsonAsync<PortfolioResponse>(cancellationToken);
        return portfolio!.Id;
    }

    private static async Task<(Guid PortfolioId, Guid AssetId)> CreatePortfolioWithAssetAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);
        var addRequest = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = "Test stock",
            Currency = "PLN",
            ManualValue = 0m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };
        var response = await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets", addRequest, cancellationToken);
        var asset = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        return (portfolioId, asset!.Id);
    }
}
