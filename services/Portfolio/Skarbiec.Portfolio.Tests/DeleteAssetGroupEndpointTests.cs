using System.Net;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The delete slice is under test, so it is called directly; the fixtures only arrange. The published events are asserted in AssetGroupOutboxTests.
public sealed class DeleteAssetGroupEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Delete_ClearsAssetsAndPublishes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var first = await client.AddAssetAsync(accountId, cancellationToken, "First");
        var second = await client.AddAssetAsync(accountId, cancellationToken, "Second");
        var group = await client.CreateAssetGroupAsync(cancellationToken);
        await client.AssignAssetsAsync(group.Id, cancellationToken, first, second);
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var strangerAccount = await stranger.CreateAccountAsync(cancellationToken, "Stranger");
        var strangerAsset = await stranger.AddAssetAsync(strangerAccount, cancellationToken, "Foreign");
        var strangerGroup = await stranger.CreateAssetGroupAsync(cancellationToken);
        await stranger.AssignAssetsAsync(strangerGroup.Id, cancellationToken, strangerAsset);
        var before = await ReadAssetGroupStatesAsync(userId, cancellationToken, first, second);
        var strangerBefore = await ReadAssetGroupStatesAsync(strangerId, cancellationToken, strangerAsset);

        var response = await client.DeleteAsync(AssetGroupUri(group.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await ReadAssetGroupStatesAsync(userId, cancellationToken, first, second);
        Assert.Equal((null, before[first].Version + 1), after[first]);
        Assert.Equal((null, before[second].Version + 1), after[second]);
        Assert.Empty(await client.ListAssetGroupsAsync(cancellationToken));
        Assert.Equal(strangerBefore[strangerAsset], (await ReadAssetGroupStatesAsync(strangerId, cancellationToken, strangerAsset))[strangerAsset]);
        Assert.Equal(1, Assert.Single(await stranger.ListAssetGroupsAsync(cancellationToken)).AssetCount);
    }
}
