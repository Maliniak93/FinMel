using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
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

    /// <summary>transactions-pln-value-and-fee-removal AC8: every listed transaction carries the
    /// asset's currency and its server-computed PLN value (<c>null</c> when no rate was known), and
    /// no fee.</summary>
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
}
