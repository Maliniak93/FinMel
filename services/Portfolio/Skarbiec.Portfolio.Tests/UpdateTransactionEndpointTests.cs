using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Portfolio.Features.UpdateTransaction;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class UpdateTransactionEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Update_ChangesQuantityAndRecomputesAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);
        var buyId = await RecordAsync(client, portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 15m, UnitPrice = 100m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, assetId, buyId), update, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        Assert.Equal(15m, body!.Quantity);
        Assert.Equal(100m, body.UnitPrice);
        Assert.Equal(15m, (await GetAssetAsync(client, portfolioId, assetId, cancellationToken)).Quantity);
        await AssertQuantityMatchesRecomputeFromScratchAsync(client, portfolioId, assetId, cancellationToken);
    }

    [Fact]
    public async Task Update_OldBuyDownwardThatBreaksLaterSell_ReturnsConflictAndNothingChanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);
        var buyId = await RecordAsync(client, portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        await RecordAsync(client, portfolioId, assetId, TransactionType.Sell, 8m, new DateOnly(2026, 1, 2), cancellationToken);
        // 10 - 8 = 2 today; dropping the Buy to 5 would let the Sell dip the running total to -3.
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 5m, UnitPrice = 100m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, assetId, buyId), update, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2m, (await GetAssetAsync(client, portfolioId, assetId, cancellationToken)).Quantity);
        var page = await ListAsync(client, portfolioId, assetId, cancellationToken);
        var untouchedBuy = page.Items.Single(t => t.Id == buyId);
        Assert.Equal(10m, untouchedBuy.Quantity);
    }

    [Fact]
    public async Task Update_ForNonExistentTransaction_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(portfolioId, assetId, Guid.NewGuid()), update, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_AssetUnderWrongPortfolio_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (portfolioId, assetId) = await CreatePortfolioWithAssetAsync(client, cancellationToken);
        var buyId = await RecordAsync(client, portfolioId, assetId, TransactionType.Buy, 10m, new DateOnly(2026, 1, 1), cancellationToken);
        var otherPortfolioId = await CreatePortfolioAsync(client, cancellationToken, "Other portfolio");
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };

        var response = await client.PutAsJsonAsync(TransactionUri(otherPortfolioId, assetId, buyId), update, cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_WithoutToken_ReturnsUnauthorized()
    {
        var update = new UpdateTransactionRequest { Type = TransactionType.Buy, Quantity = 1m, UnitPrice = 1m, Date = new DateOnly(2026, 1, 1) };
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            TransactionUri(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), update, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string TransactionUri(Guid portfolioId, Guid assetId, Guid transactionId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}/transactions/{transactionId}";

    private static async Task<Guid> RecordAsync(
        HttpClient client, Guid portfolioId, Guid assetId, TransactionType type, decimal quantity, DateOnly date, CancellationToken cancellationToken)
    {
        var request = new RecordTransactionRequest { Type = type, Quantity = quantity, UnitPrice = 10m, Date = date };
        var response = await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}/transactions", request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TransactionResponse>(cancellationToken);
        return body!.Id;
    }

    private static async Task<AssetResponse> GetAssetAsync(HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken))!;
    }

    private static async Task<PagedResponse<TransactionResponse>> ListAsync(
        HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var response = await client.GetAsync($"{PortfoliosUri}/{portfolioId}/assets/{assetId}/transactions", cancellationToken);
        return (await response.Content.ReadFromJsonAsync<PagedResponse<TransactionResponse>>(cancellationToken))!;
    }

    /// <summary>AC: "Quantity always equals recompute-from-scratch after any mutation" — re-derives
    /// <see cref="Asset.Quantity"/> from the full transaction history returned by the API and
    /// compares against what the API reports for the asset.</summary>
    private static async Task AssertQuantityMatchesRecomputeFromScratchAsync(
        HttpClient client, Guid portfolioId, Guid assetId, CancellationToken cancellationToken)
    {
        var page = await ListAsync(client, portfolioId, assetId, cancellationToken);
        var transactions = page.Items.Select(t => new Transaction
        {
            Id = t.Id,
            AssetId = t.AssetId,
            Type = t.Type,
            Quantity = t.Quantity,
            Date = t.Date
        });

        var recomputed = TransactionQuantityCalculator.Recompute(transactions);
        Assert.True(recomputed.IsSuccess);
        Assert.Equal(recomputed.Value, (await GetAssetAsync(client, portfolioId, assetId, cancellationToken)).Quantity);
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
