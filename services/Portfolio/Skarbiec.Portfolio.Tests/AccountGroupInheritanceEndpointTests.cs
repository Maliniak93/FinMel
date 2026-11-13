using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Accounts.UpdateAccount;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Auth;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// UpdateAccount and AddAsset are under test, so they are called directly; the fixtures only arrange.
public sealed class AccountGroupInheritanceEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task UpdateAccountFansOutToInheritors()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var groupA = await client.CreateAssetGroupAsync(cancellationToken, "A", ColorKey(0));
        var groupB = await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));
        var groupC = await client.CreateAssetGroupAsync(cancellationToken, "C", ColorKey(2));
        var account = await client.CreateGroupedAccountAsync(groupB.Id, cancellationToken);
        var etf = await client.AddAssetAsync(account.AccountId, cancellationToken, "Etf", AssetClass.Etf);
        var stock = await client.AddAssetAsync(account.AccountId, cancellationToken, "Stock", groupId: groupA.Id);
        var before = await ReadAssetInheritanceAsync(userId, cancellationToken, account.CashAssetId, etf, stock);

        var response = await client.PutAsJsonAsync(
            AccountUri(account.AccountId), new UpdateAccountRequest { Name = "XTB", GroupId = groupC.Id }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(groupC.Id, (await response.ReadJsonAsync(cancellationToken)).GetProperty("groupId").GetGuid());
        Assert.Equal(groupC.Id, await ReadAccountGroupAsync(userId, account.AccountId, cancellationToken));
        var after = await ReadAssetInheritanceAsync(userId, cancellationToken, account.CashAssetId, etf, stock);
        Assert.Equal((groupC.Id, false, before[account.CashAssetId].Version + 1), after[account.CashAssetId]);
        Assert.Equal((groupC.Id, false, before[etf].Version + 1), after[etf]);
        Assert.Equal(before[stock], after[stock]);
        Assert.Equal((groupA.Id, true), (after[stock].GroupId, after[stock].IsException));
    }

    [Fact]
    public async Task NewComponentInheritsAccountGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var groupB = await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));
        var groupC = await client.CreateAssetGroupAsync(cancellationToken, "C", ColorKey(2));
        var account = await client.CreateGroupedAccountAsync(groupB.Id, cancellationToken);

        var request = new Skarbiec.Portfolio.Features.AddAsset.AddAssetRequest
        {
            AccountId = account.AccountId,
            AssetClass = AssetClass.Etf,
            Name = "Etf",
            Currency = "PLN",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Etf, "PLN")
        };
        var response = await client.PostAsJsonAsync(AllAssetsUri, request, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, $"AddAsset answered {(int)response.StatusCode}.");
        var etf = (await response.ReadJsonAsync(cancellationToken)).GetProperty("id").GetGuid();
        var created = (await ReadAssetInheritanceAsync(userId, cancellationToken, etf))[etf];
        Assert.Equal((groupB.Id, false), (created.GroupId, created.IsException));

        var moved = await client.PutAsJsonAsync(
            AccountUri(account.AccountId), new UpdateAccountRequest { Name = "XTB", GroupId = groupC.Id }, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        var after = (await ReadAssetInheritanceAsync(userId, cancellationToken, etf))[etf];
        Assert.Equal((groupC.Id, false), (after.GroupId, after.IsException));
    }
}
