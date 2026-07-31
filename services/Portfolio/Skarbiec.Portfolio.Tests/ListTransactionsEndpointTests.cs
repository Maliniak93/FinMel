using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.CreatePortfolio;
using Skarbiec.Portfolio.Features.RecordTransaction;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class ListTransactionsEndpointTests(SkarbiecContainersFixture containers) : IAsyncLifetime
{
    private const string PortfoliosUri = "/api/portfolio/portfolios";

    private readonly PortfolioApiFactory _factory = new(containers);

    public ValueTask InitializeAsync() => new(_factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task List_ReturnsOnlyTransactionsOfThatAsset_NewestFirst()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);
        var assetId = await AddAssetAsync(client, portfolioId, "Asset A", cancellationToken);
        var otherAssetId = await AddAssetAsync(client, portfolioId, "Asset B", cancellationToken);

        await RecordAsync(client, portfolioId, assetId, TransactionType.Buy, new DateOnly(2026, 1, 1), cancellationToken);
        await RecordAsync(client, portfolioId, assetId, TransactionType.Buy, new DateOnly(2026, 1, 10), cancellationToken);
        await RecordAsync(client, portfolioId, otherAssetId, TransactionType.Buy, new DateOnly(2026, 1, 15), cancellationToken);

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
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);
        var assetId = await AddAssetAsync(client, portfolioId, "Asset A", cancellationToken);

        for (var day = 1; day <= 5; day++)
        {
            await RecordAsync(client, portfolioId, assetId, TransactionType.Buy, new DateOnly(2026, 1, day), cancellationToken);
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
    public async Task List_ForNonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = _factory.CreateAuthenticatedClient(Guid.NewGuid());
        var portfolioId = await CreatePortfolioAsync(client, cancellationToken);

        var response = await client.GetAsync(TransactionsUri(portfolioId, Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(TransactionsUri(Guid.NewGuid(), Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string TransactionsUri(Guid portfolioId, Guid assetId) =>
        $"{PortfoliosUri}/{portfolioId}/assets/{assetId}/transactions";

    private static async Task RecordAsync(
        HttpClient client, Guid portfolioId, Guid assetId, TransactionType type, DateOnly date, CancellationToken cancellationToken)
    {
        var request = new RecordTransactionRequest { Type = type, Quantity = 1m, UnitPrice = 10m, Date = date };
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

    private static async Task<Guid> AddAssetAsync(HttpClient client, Guid portfolioId, string name, CancellationToken cancellationToken)
    {
        var addRequest = new AddAssetRequest
        {
            AssetClass = AssetClass.Stock,
            Name = name,
            Currency = "PLN",
            ManualValue = 0m,
            ManualValueDate = new DateOnly(2026, 1, 1)
        };
        var response = await client.PostAsJsonAsync($"{PortfoliosUri}/{portfolioId}/assets", addRequest, cancellationToken);
        var asset = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        return asset!.Id;
    }
}
