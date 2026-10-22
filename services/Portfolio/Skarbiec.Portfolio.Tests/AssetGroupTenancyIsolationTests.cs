using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts.CreateAccount;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Features.Accounts.UpdateAccount;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.CashPlacesApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// Written by hand rather than on the tenancy template: a group has no GET by id, so the template's GET fact has no route to hit.
public sealed class AssetGroupTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task Update_ByStranger_ReturnsNotFoundAndLeavesGroupUnchanged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var allocation = Shares((AssetClass.Deposit, 60m), (AssetClass.Etf, 40m));
        var group = await owner.CreateAssetGroupAsync(
            cancellationToken, "Mine", ColorKey(0), 100m, monthlyContributionPln: 750m, classAllocation: allocation);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.PutAsJsonAsync(
            AssetGroupUri(group.Id), NewAssetGroupBody("Stranger edit", ColorKey(3), 5m, CushionKind, 1m), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var after = Assert.Single(await owner.ListAssetGroupsAsync(cancellationToken));
        Assert.Equal(("Mine", ColorKey(0), 100m, RegularKind, 750m), (after.Name, after.Color, after.TargetPln, after.Kind, after.MonthlyContributionPln));
        Assert.Equal(allocation, after.ClassAllocation);
        Assert.Empty(await stranger.ListAssetGroupsAsync(cancellationToken));
    }

    [Fact]
    public async Task Delete_ByStranger_ReturnsNotFoundAndKeepsAssignments()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var accountId = await owner.CreateAccountAsync(cancellationToken);
        var asset = await owner.AddAssetAsync(accountId, cancellationToken);
        var group = await owner.CreateAssetGroupAsync(cancellationToken);
        await owner.AssignAssetsAsync(group.Id, cancellationToken, asset);
        var before = await ReadAssetGroupStatesAsync(ownerId, cancellationToken, asset);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await stranger.DeleteAsync(AssetGroupUri(group.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before[asset], (await ReadAssetGroupStatesAsync(ownerId, cancellationToken, asset))[asset]);
        Assert.Equal(1, Assert.Single(await owner.ListAssetGroupsAsync(cancellationToken)).AssetCount);
    }

    [Fact]
    public async Task List_ForStranger_DoesNotIncludeGroupsOrAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await owner.CreateAccountAsync(cancellationToken);
        await owner.AddAssetAsync(accountId, cancellationToken);
        await owner.CreateAssetGroupAsync(cancellationToken);

        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var groups = await stranger.GetAsync(AssetGroupsUri, cancellationToken);
        var assets = await stranger.GetAsync(AssignableAssetsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, groups.StatusCode);
        Assert.Empty((await groups.ReadJsonAsync(cancellationToken)).EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, assets.StatusCode);
        Assert.Empty((await assets.ReadJsonAsync(cancellationToken)).EnumerateArray());
    }

    [Fact]
    public async Task Assign_AcrossOwners_ReturnsNotFoundAndChangesNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var ownerId = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(ownerId);
        var ownerAccount = await owner.CreateAccountAsync(cancellationToken);
        var ownerAsset = await owner.AddAssetAsync(ownerAccount, cancellationToken);
        var ownerGroup = await owner.CreateAssetGroupAsync(cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var strangerAccount = await stranger.CreateAccountAsync(cancellationToken, "Stranger");
        var strangerAsset = await stranger.AddAssetAsync(strangerAccount, cancellationToken, "Foreign");
        var strangerGroup = await stranger.CreateAssetGroupAsync(cancellationToken);
        var ownerBefore = await ReadAssetGroupStatesAsync(ownerId, cancellationToken, ownerAsset);
        var strangerBefore = await ReadAssetGroupStatesAsync(strangerId, cancellationToken, strangerAsset);

        var foreignGroup = await stranger.PutAsJsonAsync(AssignmentUri, NewAssignmentBody([strangerAsset], ownerGroup.Id), cancellationToken);
        var foreignAsset = await stranger.PutAsJsonAsync(AssignmentUri, NewAssignmentBody([ownerAsset], strangerGroup.Id), cancellationToken);

        await foreignGroup.AssertProblemAsync(HttpStatusCode.NotFound, AssetGroupNotFoundErrorCode, cancellationToken);
        await foreignAsset.AssertProblemAsync(HttpStatusCode.NotFound, AssetNotFoundErrorCode, cancellationToken);
        Assert.Equal(ownerBefore[ownerAsset], (await ReadAssetGroupStatesAsync(ownerId, cancellationToken, ownerAsset))[ownerAsset]);
        Assert.Equal(strangerBefore[strangerAsset], (await ReadAssetGroupStatesAsync(strangerId, cancellationToken, strangerAsset))[strangerAsset]);
    }

    [Fact]
    public async Task ForeignGroupOnCreateRequests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var foreignGroupId = (await owner.CreateAssetGroupAsync(cancellationToken)).Id;
        var strangerId = Guid.NewGuid();
        using var stranger = Factory.CreateAuthenticatedClient(strangerId);
        var strangerAccount = await stranger.CreateAccountAsync(cancellationToken, "Stranger", AccountType.Brokerage);
        var strangerBonds = await stranger.CreateAccountAsync(cancellationToken, "Stranger bonds", AccountType.Bonds);
        await stranger.FundAccountAsync(strangerBonds, cancellationToken);
        var strangerBank = await stranger.CreateBankAccountAsync(cancellationToken, "Stranger bank", balance: 10_000m);
        var strangerPlace = await stranger.CreateCashPlaceAsync(cancellationToken, "Sejf");
        var before = await SnapshotUserRowsAsync(strangerId, cancellationToken);
        var stock = new AddAssetRequest
        {
            AccountId = strangerAccount,
            AssetClass = AssetClass.Stock,
            Name = "Foreign asset",
            Currency = "PLN",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN"),
            GroupId = foreignGroupId
        };

        var responses = new[]
        {
            await stranger.PostAsJsonAsync(
                AccountsUri,
                new CreateAccountRequest { Name = "Foreign account", Type = AccountType.Brokerage, Wrapper = TaxWrapper.Regular, GroupId = foreignGroupId },
                cancellationToken),
            await stranger.PostAsJsonAsync(
                CashPlacesUri, new { name = "Foreign place", currencies = new[] { "PLN" }, groupId = foreignGroupId }, cancellationToken),
            await stranger.PostAsJsonAsync(AllAssetsUri, stock, cancellationToken),
            await stranger.PostAsJsonAsync(AllBondsUri, NewBondRequest() with { AccountId = strangerBonds, GroupId = foreignGroupId }, cancellationToken),
            await stranger.PostAsJsonAsync(AllMetalsUri, NewMetalRequest() with { GroupId = foreignGroupId }, cancellationToken),
            await stranger.PostAsJsonAsync(
                AllDepositsUri, NewDepositRequest() with { SourceAccountId = strangerBank.AccountId, GroupId = foreignGroupId }, cancellationToken),
            await stranger.PutAsJsonAsync(
                AccountUri(strangerAccount), new UpdateAccountRequest { Name = "Stranger", GroupId = foreignGroupId }, cancellationToken),
            await stranger.PutAsJsonAsync(
                AccountUri(strangerBank.AccountId), NewUpdateBankBody("Stranger bank", groupId: foreignGroupId), cancellationToken),
            await stranger.PutAsJsonAsync(CashPlaceUri(strangerPlace.Id), new { name = "Sejf", groupId = foreignGroupId }, cancellationToken),
        };

        foreach (var response in responses)
        {
            await response.AssertProblemAsync(HttpStatusCode.NotFound, AssetGroupNotFoundErrorCode, cancellationToken);
        }

        Assert.Equal(before, await SnapshotUserRowsAsync(strangerId, cancellationToken));
        await using var dbContext = CreateDbContext(strangerId);
        Assert.Equal(3, await dbContext.Set<Account>().CountAsync(cancellationToken));
        Assert.Equal(1, await dbContext.Set<CashPlace>().CountAsync(cancellationToken));
        Assert.All(await dbContext.Set<Account>().ToListAsync(cancellationToken), a => Assert.Null(a.DefaultGroupId));
        Assert.DoesNotContain(await dbContext.Assets.ToListAsync(cancellationToken), a => a.GroupId == foreignGroupId);
    }

    [Fact]
    public async Task SameNameAndColor_AcrossOwners_AreIndependent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await owner.CreateAssetGroupAsync(cancellationToken, "Poduszka", ColorKey(0));

        var response = await stranger.PostAsJsonAsync(AssetGroupsUri, NewAssetGroupBody("Poduszka", ColorKey(0)), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
