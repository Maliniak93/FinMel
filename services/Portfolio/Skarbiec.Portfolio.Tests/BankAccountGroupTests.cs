using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class BankAccountGroupTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task GroupIsBalanceGroup()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var client = Factory.CreateAuthenticatedClient(userId);
        var first = await client.CreateAssetGroupAsync(cancellationToken, "Poduszka");
        var second = await client.CreateAssetGroupAsync(cancellationToken, "Akcje", ColorKey(3));

        var created = await client.PostAsJsonAsync(AccountsUri, NewBankAccountBody("mBank", groupId: first.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.ReadJsonAsync(cancellationToken);
        var accountId = body.GetProperty("id").GetGuid();
        var moneyId = body.GetProperty("cashAssetId").GetGuid();
        Assert.Equal(first.Id, body.GetProperty("groupId").GetGuid());
        var before = (await ReadAssetGroupStatesAsync(userId, cancellationToken, moneyId))[moneyId];
        Assert.Equal(first.Id, before.GroupId);

        var updated = await client.PutAsJsonAsync(
            AccountUri(accountId), NewUpdateBankBody("mBank", groupId: second.Id), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(second.Id, (await updated.ReadJsonAsync(cancellationToken)).GetProperty("groupId").GetGuid());
        var after = (await ReadAssetGroupStatesAsync(userId, cancellationToken, moneyId))[moneyId];
        Assert.Equal(second.Id, after.GroupId);
        Assert.Equal(before.Version + 1, after.Version);
        await using var dbContext = CreateDbContext(userId);
        Assert.Null((await dbContext.Set<Account>().SingleAsync(a => a.Id == accountId, cancellationToken)).DefaultGroupId);
    }
}
