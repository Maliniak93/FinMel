using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The asset group slices are under test, so they are called directly; the fixtures only arrange.
public sealed class AssetGroupEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Crud_ReturnsShape()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var assetId = await client.AddAssetAsync(accountId, cancellationToken);

        var created = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Zeta", ColorKey(0), 1_000.5m), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var zeta = (await created.Content.ReadFromJsonAsync<AssetGroupView>(cancellationToken))!;
        Assert.Equal(("Zeta", ColorKey(0), 1_000.5m, 0), (zeta.Name, zeta.Color, zeta.TargetPln, zeta.AssetCount));
        await client.CreateAssetGroupAsync(cancellationToken, "Alfa", ColorKey(1));
        await client.AssignAssetsAsync(zeta.Id, cancellationToken, assetId);

        var listed = await client.GetAsync(AssetGroupsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var groups = (await listed.Content.ReadFromJsonAsync<List<AssetGroupView>>(cancellationToken))!;
        Assert.Equal(["Alfa", "Zeta"], groups.Select(g => g.Name).ToArray());
        Assert.Equal((ColorKey(1), (decimal?)null, 0), (groups[0].Color, groups[0].TargetPln, groups[0].AssetCount));
        Assert.Equal((ColorKey(0), 1_000.5m, 1), (groups[1].Color, groups[1].TargetPln, groups[1].AssetCount));

        var updated = await client.PutAsJsonAsync(AssetGroupUri(zeta.Id), NewAssetGroupBody("Beta", ColorKey(2), 2_000m), cancellationToken);

        Assert.True(updated.IsSuccessStatusCode);
        var afterUpdate = await client.ListAssetGroupsAsync(cancellationToken);
        Assert.Equal(["Alfa", "Beta"], afterUpdate.Select(g => g.Name).ToArray());
        var beta = Assert.Single(afterUpdate, g => g.Id == zeta.Id);
        Assert.Equal((ColorKey(2), 2_000m, 1), (beta.Color, beta.TargetPln, beta.AssetCount));

        var deleted = await client.DeleteAsync(AssetGroupUri(zeta.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(["Alfa"], (await client.ListAssetGroupsAsync(cancellationToken)).Select(g => g.Name).ToArray());
    }

    [Fact]
    public async Task DuplicateName_IgnoresCase_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var poduszka = await client.CreateAssetGroupAsync(cancellationToken, "Poduszka", ColorKey(0));
        var other = await client.CreateAssetGroupAsync(cancellationToken, "Inna", ColorKey(1));

        var create = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("poduszka", ColorKey(2)), cancellationToken);
        var rename = await client.PutAsJsonAsync(AssetGroupUri(other.Id), NewAssetGroupBody("PODUSZKA", ColorKey(1)), cancellationToken);
        var recase = await client.PutAsJsonAsync(AssetGroupUri(poduszka.Id), NewAssetGroupBody("poduszka", ColorKey(0)), cancellationToken);

        await create.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DuplicateNameErrorCode, cancellationToken);
        await rename.AssertProblemAsync(HttpStatusCode.Conflict, PortfolioAssertions.DuplicateNameErrorCode, cancellationToken);
        Assert.True(recase.IsSuccessStatusCode);
        var groups = await client.ListAssetGroupsAsync(cancellationToken);
        Assert.Equal(["Inna", "poduszka"], groups.Select(g => g.Name).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Create13th_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        for (var i = 0; i < GroupLimit; i++)
        {
            await client.CreateAssetGroupAsync(cancellationToken, $"Group {i}", ColorKey(i));
        }

        var response = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Group 12", ColorKey(0)), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.Conflict, GroupLimitReachedErrorCode, cancellationToken);
        Assert.Equal(GroupLimit, (await client.ListAssetGroupsAsync(cancellationToken)).Count);
    }

    [Fact]
    public async Task InvalidColorOrTarget()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var existing = await client.CreateAssetGroupAsync(cancellationToken, "Existing", ColorKey(1));

        var unknownColor = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Nowa", "no-such-colour"), cancellationToken);
        var zeroTarget = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), 0m), cancellationToken);
        var negativeTarget = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), -5m), cancellationToken);
        var threeDecimals = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Nowa", ColorKey(0), 10.123m), cancellationToken);
        var updateUnknownColor = await client.PutAsJsonAsync(AssetGroupUri(existing.Id), NewAssetGroupBody("Existing", "no-such-colour"), cancellationToken);
        var updateZeroTarget = await client.PutAsJsonAsync(AssetGroupUri(existing.Id), NewAssetGroupBody("Existing", ColorKey(1), 0m), cancellationToken);

        await unknownColor.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidColorErrorCode, cancellationToken);
        await zeroTarget.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidTargetErrorCode, cancellationToken);
        await negativeTarget.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidTargetErrorCode, cancellationToken);
        await threeDecimals.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidTargetErrorCode, cancellationToken);
        await updateUnknownColor.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidColorErrorCode, cancellationToken);
        await updateZeroTarget.AssertProblemAsync(HttpStatusCode.BadRequest, InvalidTargetErrorCode, cancellationToken);
        Assert.Equal(["Existing"], (await client.ListAssetGroupsAsync(cancellationToken)).Select(g => g.Name).ToArray());
    }

    [Fact]
    public async Task ColorTaken_Returns409()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAssetGroupAsync(cancellationToken, "Pierwsza", ColorKey(0));
        var second = await client.CreateAssetGroupAsync(cancellationToken, "Druga", ColorKey(1));

        var create = await client.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Trzecia", ColorKey(0)), cancellationToken);
        var recolour = await client.PutAsJsonAsync(AssetGroupUri(second.Id), NewAssetGroupBody("Druga", ColorKey(0)), cancellationToken);
        var keepOwn = await client.PutAsJsonAsync(AssetGroupUri(second.Id), NewAssetGroupBody("Druga", ColorKey(1), 500m), cancellationToken);

        await create.AssertProblemAsync(HttpStatusCode.Conflict, GroupColorTakenErrorCode, cancellationToken);
        await recolour.AssertProblemAsync(HttpStatusCode.Conflict, GroupColorTakenErrorCode, cancellationToken);
        Assert.True(keepOwn.IsSuccessStatusCode);
    }
}
