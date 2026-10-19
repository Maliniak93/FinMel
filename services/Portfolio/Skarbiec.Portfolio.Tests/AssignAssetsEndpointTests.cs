using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The assignment slice is under test, so it is called directly; the fixtures only arrange. The published events are asserted in AssetGroupOutboxTests.
public sealed class AssignAssetsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Assign_PublishesChangedAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var first = await client.AddAssetAsync(accountId, cancellationToken, "First");
        var second = await client.AddAssetAsync(accountId, cancellationToken, "Second");
        var untouched = await client.AddAssetAsync(accountId, cancellationToken, "Untouched");
        var group = await client.CreateAssetGroupAsync(cancellationToken);
        var before = await ReadAssetGroupStatesAsync(userId, cancellationToken, first, second, untouched);

        var response = await client.PutAsJsonAsync(AssignmentUri, NewAssignmentBody([first, second], group.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await ReadAssetGroupStatesAsync(userId, cancellationToken, first, second, untouched);
        Assert.Equal((group.Id, before[first].Version + 1), after[first]);
        Assert.Equal((group.Id, before[second].Version + 1), after[second]);
        Assert.Equal(before[untouched], after[untouched]);
        Assert.Equal(2, Assert.Single(await client.ListAssetGroupsAsync(cancellationToken)).AssetCount);
    }

    [Fact]
    public async Task Clear_PublishesChangedAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var asset = await client.AddAssetAsync(accountId, cancellationToken);
        var group = await client.CreateAssetGroupAsync(cancellationToken);
        await client.AssignAssetsAsync(group.Id, cancellationToken, asset);
        var before = await ReadAssetGroupStatesAsync(userId, cancellationToken, asset);

        var response = await client.PutAsJsonAsync(AssignmentUri, NewAssignmentBody([asset], null), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var after = await ReadAssetGroupStatesAsync(userId, cancellationToken, asset);
        Assert.Equal((null, before[asset].Version + 1), after[asset]);
        Assert.Equal(0, Assert.Single(await client.ListAssetGroupsAsync(cancellationToken)).AssetCount);
    }

    [Fact]
    public async Task SameGroup_PublishesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var asset = await client.AddAssetAsync(accountId, cancellationToken);
        var group = await client.CreateAssetGroupAsync(cancellationToken);
        await client.AssignAssetsAsync(group.Id, cancellationToken, asset);
        var before = await ReadAssetGroupStatesAsync(userId, cancellationToken, asset);

        var response = await client.PutAsJsonAsync(AssignmentUri, NewAssignmentBody([asset], group.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(before[asset], (await ReadAssetGroupStatesAsync(userId, cancellationToken, asset))[asset]);
    }

    [Fact]
    public async Task Guards()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var asset = await client.AddAssetAsync(accountId, cancellationToken);
        var group = await client.CreateAssetGroupAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerAccount = await stranger.CreateAccountAsync(cancellationToken, "Stranger");
        var foreignAsset = await stranger.AddAssetAsync(strangerAccount, cancellationToken, "Foreign");
        var before = await ReadAssetGroupStatesAsync(userId, cancellationToken, asset);

        var unknownGroup = await client.PutAsJsonAsync(AssignmentUri, NewAssignmentBody([asset], Guid.NewGuid()), cancellationToken);
        var foreignInList = await client.PutAsJsonAsync(AssignmentUri, NewAssignmentBody([asset, foreignAsset], group.Id), cancellationToken);
        var unknownAsset = await client.PutAsJsonAsync(AssignmentUri, NewAssignmentBody([asset, Guid.NewGuid()], group.Id), cancellationToken);
        var empty = await client.PutAsJsonAsync(AssignmentUri, NewAssignmentBody([], group.Id), cancellationToken);

        await unknownGroup.AssertProblemAsync(HttpStatusCode.NotFound, AssetGroupNotFoundErrorCode, cancellationToken);
        await foreignInList.AssertProblemAsync(HttpStatusCode.NotFound, AssetNotFoundErrorCode, cancellationToken);
        await unknownAsset.AssertProblemAsync(HttpStatusCode.NotFound, AssetNotFoundErrorCode, cancellationToken);
        await empty.AssertProblemAsync(HttpStatusCode.BadRequest, AssetIdsRequiredErrorCode, cancellationToken);
        Assert.Equal(before[asset], (await ReadAssetGroupStatesAsync(userId, cancellationToken, asset))[asset]);
    }

    [Fact]
    public async Task ManagedAndArchivedAssets_CanBeAssigned()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var accountId = await client.CreateAccountAsync(cancellationToken);
        var accountCash = await client.GetAccountCashAssetIdAsync(accountId, cancellationToken);
        var bankBalance = (await client.CreateBankAccountAsync(cancellationToken)).MoneyAssetId;
        var placeBalance = await client.AddCashAssetAsync(cancellationToken);
        var archived = await client.AddAssetAsync(accountId, cancellationToken, "Archived");
        await client.ArchiveAssetAsync(archived, cancellationToken);
        var group = await client.CreateAssetGroupAsync(cancellationToken);

        var response = await client.PutAsJsonAsync(
            AssignmentUri, NewAssignmentBody([accountCash, bankBalance, placeBalance, archived], group.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var states = await ReadAssetGroupStatesAsync(userId, cancellationToken, accountCash, bankBalance, placeBalance, archived);
        Assert.Equal(4, states.Count);
        Assert.All(states.Values, state => Assert.Equal(group.Id, state.GroupId));
    }
}
