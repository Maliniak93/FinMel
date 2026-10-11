using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class GetAssetEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Get_ExistingAsset_ReturnsIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken, name: "Shares");

        var response = await client.GetAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(assetId, body!.Id);
        Assert.Equal("Shares", body.Name);
    }

    [Fact]
    public async Task Get_NonExistentAsset_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(AssetUri(Guid.NewGuid()), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_AssetWithTransactions_ReportsTransactionCountFromTransactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var (accountId, assetId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 1), cancellationToken);
        await client.RecordTransactionAsync(assetId, TransactionType.Buy, 1m, new DateOnly(2026, 1, 2), cancellationToken);

        var response = await client.GetAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal(2, body!.TransactionCount);
    }

    [Fact]
    public async Task Get_AssetWithOutOfSetCurrency_ReadsBackUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext(ownerId))
        {
            seedContext.Assets.Add(new Asset
            {
                Id = assetId,
                AssetClass = AssetClass.Cash,
                ValuationMode = AssetValuationMode.CurrencyValued,
                Name = "Legacy GBP cash",
                Currency = "GBP"
            });
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(ownerId);
        var response = await client.GetAsync(AssetUri(assetId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AssetResponse>(cancellationToken);
        Assert.Equal("GBP", body!.Currency);
    }
}
