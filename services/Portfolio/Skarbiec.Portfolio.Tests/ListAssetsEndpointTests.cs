using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ListAssetsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_FilteredByAccount_ReturnsOnlyAssetsOfThatAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountA = await client.CreateAccountAsync(cancellationToken, "A");
        var accountB = await client.CreateAccountAsync(cancellationToken, "B");
        await client.AddStockInAccountAsync(accountA, cancellationToken, "Asset in A");
        await client.AddStockInAccountAsync(accountB, cancellationToken, "Asset in B");
        await client.AddCashAssetAsync(cancellationToken, name: "Asset in none");

        var response = await client.GetAsync(AssetsUri(accountA), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        var asset = Assert.Single(assets!);
        Assert.Equal("Asset in A", asset.Name);
        Assert.Equal(accountA, asset.AccountId);
    }

    [Fact]
    public async Task List_WithoutAccountFilter_ReturnsEveryAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var account = await client.CreateAccountAsync(cancellationToken);
        await client.AddStockInAccountAsync(account, cancellationToken, "In account");
        await client.AddDepositAsync(cancellationToken);

        var response = await client.GetAsync(AllAssetsUri, cancellationToken);

        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        Assert.Equal(2, assets!.Count);
        Assert.Contains(assets, a => a.AccountId == null);
    }

    [Fact]
    public async Task List_IncludesArchivedAssetWithFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var liveId = await client.AddAssetAsync(accountId, cancellationToken, name: "Live");
        var archivedId = await client.AddAssetAsync(accountId, cancellationToken, name: "Archived");
        await client.ArchiveAssetAsync(archivedId, cancellationToken);

        var response = await client.GetAsync(AssetsUri(accountId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        Assert.Equal(2, assets!.Count);
        Assert.False(assets.Single(a => a.Id == liveId).IsArchived);
        Assert.True(assets.Single(a => a.Id == archivedId).IsArchived);
    }

    [Fact]
    public async Task List_AccountWithNoAssets_ReturnsEmptyList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);

        var response = await client.GetAsync(AssetsUri(accountId), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        Assert.Empty(assets!);
    }

    [Fact]
    public async Task List_NonExistentAccount_ReturnsNotFound()
    {
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(AssetsUri(Guid.NewGuid()), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_IncludesAssetWithOutOfSetCurrency()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var assetId = Guid.NewGuid();

        await using (var seedContext = CreateDbContext(ownerId))
        {
            seedContext.Assets.Add(new Asset
            {
                Id = assetId,
                AssetClass = AssetClass.Stock,
                ValuationMode = AssetValuationMode.Market,
                InstrumentId = Guid.NewGuid(),
                Name = "Legacy GBP shares",
                Currency = "GBP"
            });
            await seedContext.SaveChangesAsync(cancellationToken);
        }

        using var client = Factory.CreateAuthenticatedClient(ownerId);
        var response = await client.GetAsync(AllAssetsUri, cancellationToken);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var asset = Assert.Single(assets!);
        Assert.Equal("GBP", asset.Currency);
    }

    [Fact]
    public async Task List_WithTermDeposit_CarriesDepositMaturityDateOnlyOnTheDeposit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var deposit = await client.AddDepositAsync(cancellationToken);
        var (_, stockId) = await client.CreateAccountWithAssetAsync(cancellationToken);

        var response = await client.GetAsync(AllAssetsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        Assert.NotNull(assets);
        Assert.Equal(new DateOnly(2026, 4, 15), assets.Single(a => a.Id == deposit.AssetId).DepositMaturityDate);
        Assert.Null(assets.Single(a => a.Id == stockId).DepositMaturityDate);
    }

    [Fact]
    public async Task List_WithSettledTermDeposit_CarriesDepositSettled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(AfterDefaultMaturityUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var settled = await client.AddDepositAsync(cancellationToken, NewDepositRequest(name: "Settled"));
        var due = await client.AddDepositAsync(cancellationToken, NewDepositRequest(name: "Due"));
        var (_, stockId) = await client.CreateAccountWithAssetAsync(cancellationToken);
        await client.SettleDepositAsync(settled.AssetId, cancellationToken);

        var response = await client.GetAsync(AllAssetsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = await response.Content.ReadFromJsonAsync<List<AssetResponse>>(cancellationToken);
        Assert.NotNull(assets);
        Assert.True(assets.Single(a => a.Id == settled.AssetId).DepositSettled);
        Assert.False(assets.Single(a => a.Id == due.AssetId).DepositSettled);
        Assert.Null(assets.Single(a => a.Id == stockId).DepositSettled);
    }
}
