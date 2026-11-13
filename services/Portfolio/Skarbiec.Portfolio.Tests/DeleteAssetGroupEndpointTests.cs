using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts.CreateAccount;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.CashPlacesApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The delete slice is under test, so it is called directly; the fixtures only arrange. The published events are asserted in AssetGroupOutboxTests.
public sealed class DeleteAssetGroupEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Delete_ClearsDefaults()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var group = await client.CreateAssetGroupAsync(cancellationToken);
        var account = await client.PostAsJsonAsync(
            AccountsUri,
            new CreateAccountRequest { Name = "XTB", Type = AccountType.Brokerage, Wrapper = TaxWrapper.Regular, GroupId = group.Id },
            cancellationToken);
        var accountBody = await account.ReadJsonAsync(cancellationToken);
        var accountId = accountBody.GetProperty("id").GetGuid();
        var cashId = accountBody.GetProperty("cashAssetId").GetGuid();
        var place = await client.PostAsJsonAsync(
            CashPlacesUri, new { name = "Sejf", currencies = new[] { "PLN" }, groupId = group.Id }, cancellationToken);
        var placeId = (await place.ReadJsonAsync(cancellationToken)).GetProperty("id").GetGuid();
        await using var dbContext = CreateDbContext(userId);
        var balanceId = await dbContext.Assets.Where(a => a.CashPlaceId == placeId).Select(a => a.Id).SingleAsync(cancellationToken);

        var response = await client.DeleteAsync(AssetGroupUri(group.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var storedAccount = await client.GetAsync(AccountUri(accountId), cancellationToken);
        Assert.Equal(JsonValueKind.Null, (await storedAccount.ReadJsonAsync(cancellationToken)).GetProperty("groupId").ValueKind);
        var storedPlace = await client.GetAsync(CashPlaceUri(placeId), cancellationToken);
        Assert.Equal(JsonValueKind.Null, (await storedPlace.ReadJsonAsync(cancellationToken)).GetProperty("groupId").ValueKind);
        var states = await ReadAssetGroupStatesAsync(userId, cancellationToken, cashId, balanceId);
        Assert.Null(states[cashId].GroupId);
        Assert.Null(states[balanceId].GroupId);
    }

    [Fact]
    public async Task DeletingAccountGroupAndExceptions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var groupA = await client.CreateAssetGroupAsync(cancellationToken, "A", ColorKey(0));
        var groupB = await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));
        var inAGroup = await client.CreateGroupedAccountAsync(groupA.Id, cancellationToken, "In A");
        var inheritor = await client.AddAssetAsync(inAGroup.AccountId, cancellationToken, "Inheritor", AssetClass.Etf);
        var inBGroup = await client.CreateGroupedAccountAsync(groupB.Id, cancellationToken, "In B");
        var exception = await client.AddAssetAsync(inBGroup.AccountId, cancellationToken, "Exception", groupId: groupA.Id);
        var before = await ReadAssetInheritanceAsync(
            userId, cancellationToken, inAGroup.CashAssetId, inheritor, inBGroup.CashAssetId, exception);

        var response = await client.DeleteAsync(AssetGroupUri(groupA.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(await ReadAccountGroupAsync(userId, inAGroup.AccountId, cancellationToken));
        Assert.Equal(groupB.Id, await ReadAccountGroupAsync(userId, inBGroup.AccountId, cancellationToken));
        var after = await ReadAssetInheritanceAsync(
            userId, cancellationToken, inAGroup.CashAssetId, inheritor, inBGroup.CashAssetId, exception);
        Assert.Equal((null, false, before[inAGroup.CashAssetId].Version + 1), after[inAGroup.CashAssetId]);
        Assert.Equal((null, false, before[inheritor].Version + 1), after[inheritor]);
        Assert.Equal((null, true, before[exception].Version + 1), after[exception]);
        Assert.Equal(before[inBGroup.CashAssetId], after[inBGroup.CashAssetId]);
    }

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
