using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;

namespace Skarbiec.Portfolio.Tests;

// The members slice is under test, so it is called directly; the fixtures only arrange.
public sealed class SetAssetGroupMembersEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task RemovingAccountKeepsExceptions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var groupA = await client.CreateAssetGroupAsync(cancellationToken, "A", ColorKey(0));
        var groupB = await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));
        var bonds = await client.CreateGroupedBondsAccountAsync(groupB.Id, cancellationToken, groupA.Id);
        var exception = bonds.ExceptionBond!.Value;

        var response = await client.PutAsJsonAsync(MembersUri(groupB.Id), NewMembersBody(), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Members answered {(int)response.StatusCode}.");
        Assert.Null(await ReadAccountGroupAsync(userId, bonds.AccountId, cancellationToken));
        var states = await ReadAssetInheritanceAsync(
            userId, cancellationToken, bonds.CashAssetId, bonds.FirstBond, bonds.SecondBond, exception);
        Assert.Equal((null, false), (states[bonds.CashAssetId].GroupId, states[bonds.CashAssetId].IsException));
        Assert.Equal((null, false), (states[bonds.FirstBond].GroupId, states[bonds.FirstBond].IsException));
        Assert.Equal((null, false), (states[bonds.SecondBond].GroupId, states[bonds.SecondBond].IsException));
        Assert.Equal((groupA.Id, true), (states[exception].GroupId, states[exception].IsException));
    }

    [Fact]
    public async Task CheckAndUncheckFollowTheRule()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var groupA = await client.CreateAssetGroupAsync(cancellationToken, "A", ColorKey(0));
        var groupB = await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));
        var bonds = await client.CreateGroupedBondsAccountAsync(groupB.Id, cancellationToken);

        var check = await client.PutAsJsonAsync(MembersUri(groupA.Id), NewMembersBody(assetIds: [bonds.FirstBond]), cancellationToken);

        Assert.True(check.IsSuccessStatusCode, $"Check answered {(int)check.StatusCode}.");
        var exception = (await ReadAssetInheritanceAsync(userId, cancellationToken, bonds.FirstBond))[bonds.FirstBond];
        Assert.Equal((groupA.Id, true), (exception.GroupId, exception.IsException));

        var uncheck = await client.PutAsJsonAsync(MembersUri(groupA.Id), NewMembersBody(), cancellationToken);

        Assert.True(uncheck.IsSuccessStatusCode, $"Uncheck answered {(int)uncheck.StatusCode}.");
        var inheriting = (await ReadAssetInheritanceAsync(userId, cancellationToken, bonds.FirstBond))[bonds.FirstBond];
        Assert.Equal((groupB.Id, false), (inheriting.GroupId, inheriting.IsException));

        var omitted = await client.PutAsJsonAsync(
            MembersUri(groupB.Id), NewMembersBody([bonds.AccountId], [bonds.SecondBond]), cancellationToken);

        Assert.True(omitted.IsSuccessStatusCode, $"Omit answered {(int)omitted.StatusCode}.");
        var states = await ReadAssetInheritanceAsync(userId, cancellationToken, bonds.FirstBond, bonds.SecondBond);
        Assert.Equal((null, true), (states[bonds.FirstBond].GroupId, states[bonds.FirstBond].IsException));
        Assert.Equal((groupB.Id, false), (states[bonds.SecondBond].GroupId, states[bonds.SecondBond].IsException));
        Assert.Equal(groupB.Id, await ReadAccountGroupAsync(userId, bonds.AccountId, cancellationToken));
    }

    [Fact]
    public async Task ListingAccountOfAnotherGroupMovesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var groupA = await client.CreateAssetGroupAsync(cancellationToken, "A", ColorKey(0));
        var groupB = await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));
        var groupC = await client.CreateAssetGroupAsync(cancellationToken, "C", ColorKey(2));
        var bonds = await client.CreateGroupedBondsAccountAsync(groupA.Id, cancellationToken, groupC.Id);
        var exception = bonds.ExceptionBond!.Value;

        var response = await client.PutAsJsonAsync(
            MembersUri(groupB.Id), NewMembersBody([bonds.AccountId], [bonds.FirstBond, bonds.SecondBond]), cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"Members answered {(int)response.StatusCode}.");
        Assert.Equal(groupB.Id, await ReadAccountGroupAsync(userId, bonds.AccountId, cancellationToken));
        var states = await ReadAssetInheritanceAsync(
            userId, cancellationToken, bonds.CashAssetId, bonds.FirstBond, bonds.SecondBond, exception);
        Assert.Equal((groupB.Id, false), (states[bonds.CashAssetId].GroupId, states[bonds.CashAssetId].IsException));
        Assert.Equal((groupB.Id, false), (states[bonds.FirstBond].GroupId, states[bonds.FirstBond].IsException));
        Assert.Equal((groupB.Id, false), (states[bonds.SecondBond].GroupId, states[bonds.SecondBond].IsException));
        Assert.Equal((groupC.Id, true), (states[exception].GroupId, states[exception].IsException));
    }

    [Fact]
    public async Task RejectsMoneyAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var group = await client.CreateAssetGroupAsync(cancellationToken);
        var account = await client.CreateGroupedAccountAsync(null, cancellationToken);
        var before = await ReadAssetInheritanceAsync(userId, cancellationToken, account.CashAssetId);

        var members = await client.PutAsJsonAsync(
            MembersUri(group.Id), NewMembersBody(assetIds: [account.CashAssetId]), cancellationToken);
        var assignment = await client.PutAsJsonAsync(
            AssignmentUri, NewAssignmentBody([account.CashAssetId], group.Id), cancellationToken);

        await members.AssertProblemAsync(HttpStatusCode.BadRequest, AccountCashManagedErrorCode, cancellationToken);
        await assignment.AssertProblemAsync(HttpStatusCode.BadRequest, AccountCashManagedErrorCode, cancellationToken);
        Assert.Equal(before, await ReadAssetInheritanceAsync(userId, cancellationToken, account.CashAssetId));
    }
}
