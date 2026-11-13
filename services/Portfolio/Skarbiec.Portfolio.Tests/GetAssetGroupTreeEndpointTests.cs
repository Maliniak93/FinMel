using System.Net;
using System.Text.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The tree slice is under test, so it is called directly; the fixtures only arrange.
public sealed class GetAssetGroupTreeEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task ReturnsAccountsWithComponents()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var groupA = await client.CreateAssetGroupAsync(cancellationToken, "A", ColorKey(0));
        var groupB = await client.CreateAssetGroupAsync(cancellationToken, "B", ColorKey(1));
        var broker = await client.CreateGroupedAccountAsync(groupB.Id, cancellationToken, "Broker");
        var stock = await client.AddAssetAsync(broker.AccountId, cancellationToken, "Stock");
        var etf = await client.AddAssetAsync(broker.AccountId, cancellationToken, "Etf", AssetClass.Etf, groupId: groupA.Id);
        var bonds = await client.CreateGroupedBondsAccountAsync(groupB.Id, cancellationToken, name: "Bonds");
        var bank = await client.CreateBankAccountAsync(cancellationToken, "Bank", groupId: groupB.Id);
        var deposit = await client.AddDepositAsync(cancellationToken);
        var metal = await client.AddMetalAsync(cancellationToken);
        var placeBalance = await client.AddCashAssetAsync(cancellationToken);

        var response = await client.GetAsync(TreeUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = TreeItems(await response.ReadJsonAsync(cancellationToken));
        var accountItems = items.Where(i => i.GetProperty("kind").GetString() == "Account").ToList();
        var assetItems = items.Where(i => i.GetProperty("kind").GetString() == "Asset").ToList();
        Assert.Equal(items.Count, accountItems.Count + assetItems.Count);
        Assert.Equal(4, accountItems.Count);
        Assert.Equal(
            new[] { deposit.AssetId, metal.AssetId, placeBalance }.Order(),
            assetItems.Select(i => i.GetProperty("assetId").GetGuid()).Order());

        var brokerItem = Assert.Single(accountItems, i => i.GetProperty("accountId").GetGuid() == broker.AccountId);
        Assert.Equal("Broker", brokerItem.GetProperty("name").GetString());
        Assert.Equal(broker.CashAssetId, brokerItem.GetProperty("cashAssetId").GetGuid());
        Assert.Equal(groupB.Id, brokerItem.GetProperty("groupId").GetGuid());
        var brokerChildren = TreeChildren(brokerItem);
        Assert.Equal(2, brokerChildren.Count);
        var stockChild = Assert.Single(brokerChildren, c => c.GetProperty("assetId").GetGuid() == stock);
        Assert.Equal(groupB.Id, stockChild.GetProperty("groupId").GetGuid());
        Assert.False(stockChild.GetProperty("isException").GetBoolean());
        var etfChild = Assert.Single(brokerChildren, c => c.GetProperty("assetId").GetGuid() == etf);
        Assert.Equal(groupA.Id, etfChild.GetProperty("groupId").GetGuid());
        Assert.True(etfChild.GetProperty("isException").GetBoolean());

        var bondsItem = Assert.Single(accountItems, i => i.GetProperty("accountId").GetGuid() == bonds.AccountId);
        Assert.Equal(
            new[] { bonds.FirstBond, bonds.SecondBond }.Order(),
            TreeChildren(bondsItem).Select(c => c.GetProperty("assetId").GetGuid()).Order());

        var bankItem = Assert.Single(accountItems, i => i.GetProperty("accountId").GetGuid() == bank.AccountId);
        Assert.Equal(bank.MoneyAssetId, bankItem.GetProperty("cashAssetId").GetGuid());
        Assert.Equal(groupB.Id, bankItem.GetProperty("groupId").GetGuid());
        Assert.Empty(TreeChildren(bankItem));

        var moneyIds = new HashSet<Guid> { broker.CashAssetId, bonds.CashAssetId, bank.MoneyAssetId };
        Assert.DoesNotContain(
            items.Concat(items.SelectMany(TreeChildren)),
            i => i.OptionalGuid("assetId") is { } id && moneyIds.Contains(id));
    }
}
