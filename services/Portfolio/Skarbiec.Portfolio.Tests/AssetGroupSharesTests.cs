using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioAssertions;

namespace Skarbiec.Portfolio.Tests;

// The shares slice is under test, so it is called directly; the fixtures only arrange.
public sealed class AssetGroupSharesTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Create_FirstRegularGroupTakes100AndNewOnesStartAtZero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var first = await client.CreateAssetGroupAsync(cancellationToken, "Pierwsza", ColorKey(0));
        var second = await client.CreateAssetGroupAsync(cancellationToken, "Druga", ColorKey(1));
        var cushion = await client.CreateAssetGroupAsync(cancellationToken, "Poduszka", ColorKey(2), 12_000m, CushionKind);

        Assert.Equal(100m, first.SharePercent);
        Assert.Equal(0m, second.SharePercent);
        Assert.Null(cushion.SharePercent);
        var shares = await client.ReadSharesAsync(cancellationToken);
        Assert.Equal(100m, shares[first.Id]);
        Assert.Equal(0m, shares[second.Id]);
        Assert.Null(shares[cushion.Id]);
    }

    [Fact]
    public async Task Delete_HandsTheShareToTheRestProportionally()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateAssetGroupAsync(cancellationToken, "A", ColorKey(0));
        var b = await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));
        var c = await client.CreateAssetGroupAsync(cancellationToken, "C", ColorKey(2));
        await client.SaveGroupSharesAsync(cancellationToken, (a.Id, 50m), (b.Id, 30m), (c.Id, 20m));

        var deleted = await client.DeleteAsync(AssetGroupUri(c.Id), cancellationToken);

        Assert.True(deleted.IsSuccessStatusCode);
        var shares = await client.ReadSharesAsync(cancellationToken);
        Assert.Equal(62.5m, shares[a.Id]);
        Assert.Equal(37.5m, shares[b.Id]);
    }

    [Fact]
    public async Task Delete_OfTheLastRegularGroupLeavesNoShares()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var regular = await client.CreateAssetGroupAsync(cancellationToken, "Zwykła", ColorKey(0));
        var cushion = await client.CreateAssetGroupAsync(cancellationToken, "Poduszka", ColorKey(1), 12_000m, CushionKind);

        var deleted = await client.DeleteAsync(AssetGroupUri(regular.Id), cancellationToken);

        Assert.True(deleted.IsSuccessStatusCode);
        var remaining = Assert.Single(await client.ListAssetGroupsAsync(cancellationToken));
        Assert.Equal(cushion.Id, remaining.Id);
        Assert.Null(remaining.SharePercent);
    }

    [Fact]
    public async Task Update_RegularToCushionHandsTheShareToTheRest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateAssetGroupAsync(cancellationToken, "A", ColorKey(0));
        var b = await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));
        await client.SaveGroupSharesAsync(cancellationToken, (a.Id, 60m), (b.Id, 40m));

        var turned = await client.UpdateAssetGroupAsync(b.Id, cancellationToken, "B", ColorKey(1), 5_000m, CushionKind);

        Assert.Null(turned.SharePercent);
        var shares = await client.ReadSharesAsync(cancellationToken);
        Assert.Equal(100m, shares[a.Id]);
        Assert.Null(shares[b.Id]);
    }

    [Fact]
    public async Task Update_CushionToRegularTakes100WhenFirstAndStartsAtZeroOtherwise()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var cushion = await client.CreateAssetGroupAsync(cancellationToken, "Poduszka", ColorKey(0), 12_000m, CushionKind);

        var asFirst = await client.UpdateAssetGroupAsync(cushion.Id, cancellationToken, "Poduszka", ColorKey(0), 12_000m);
        var other = await client.CreateAssetGroupAsync(cancellationToken, "Inna", ColorKey(1), 1_000m, CushionKind);
        var asSecond = await client.UpdateAssetGroupAsync(other.Id, cancellationToken, "Inna", ColorKey(1), 1_000m);

        Assert.Equal(100m, asFirst.SharePercent);
        Assert.Equal(0m, asSecond.SharePercent);
        Assert.Equal(100m, (await client.ReadSharesAsync(cancellationToken))[cushion.Id]);
    }

    [Fact]
    public async Task SaveShares_StoresTheWholeList()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateAssetGroupAsync(cancellationToken, "A", ColorKey(0));
        var b = await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));
        await client.CreateAssetGroupAsync(cancellationToken, "Poduszka", ColorKey(2), 12_000m, CushionKind);

        var response = await client.PutAsJsonAsync(SharesUri, NewSharesBody((a.Id, 30.5m), (b.Id, 69.5m)), cancellationToken);

        Assert.True(response.IsSuccessStatusCode);
        var shares = await client.ReadSharesAsync(cancellationToken);
        Assert.Equal(30.5m, shares[a.Id]);
        Assert.Equal(69.5m, shares[b.Id]);
    }

    [Fact]
    public async Task SaveShares_WithAWrongSum_Returns400AndKeepsTheShares()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateAssetGroupAsync(cancellationToken, "A", ColorKey(0));
        var b = await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));

        var response = await client.PutAsJsonAsync(SharesUri, NewSharesBody((a.Id, 60m), (b.Id, 30m)), cancellationToken);

        await response.AssertProblemAsync(HttpStatusCode.BadRequest, SharesSumErrorCode, cancellationToken);
        var shares = await client.ReadSharesAsync(cancellationToken);
        Assert.Equal(100m, shares[a.Id]);
        Assert.Equal(0m, shares[b.Id]);
    }

    [Fact]
    public async Task SaveShares_WithAGroupSetThatIsNotExactlyTheRegularGroups_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var a = await client.CreateAssetGroupAsync(cancellationToken, "A", ColorKey(0));
        await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));
        var cushion = await client.CreateAssetGroupAsync(cancellationToken, "Poduszka", ColorKey(2), 12_000m, CushionKind);

        var missing = await client.PutAsJsonAsync(SharesUri, NewSharesBody((a.Id, 100m)), cancellationToken);
        var withCushion = await client.PutAsJsonAsync(
            SharesUri, NewSharesBody((a.Id, 50m), (cushion.Id, 50m)), cancellationToken);
        var unknown = await client.PutAsJsonAsync(
            SharesUri, NewSharesBody((a.Id, 50m), (Guid.NewGuid(), 50m)), cancellationToken);

        await missing.AssertProblemAsync(HttpStatusCode.BadRequest, SharesGroupMismatchErrorCode, cancellationToken);
        await withCushion.AssertProblemAsync(HttpStatusCode.BadRequest, SharesGroupMismatchErrorCode, cancellationToken);
        await unknown.AssertProblemAsync(HttpStatusCode.BadRequest, SharesGroupMismatchErrorCode, cancellationToken);
    }
}
