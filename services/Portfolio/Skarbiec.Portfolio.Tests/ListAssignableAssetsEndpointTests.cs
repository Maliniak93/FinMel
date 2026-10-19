using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.BankAccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.CashPlacesApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

// The list slice is under test, so it is called directly; the fixtures only arrange.
public sealed class ListAssignableAssetsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task ReturnsEveryAsset()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken, "XTB");
        var accountCash = await client.GetAccountCashAssetIdAsync(accountId, cancellationToken);
        var stock = await client.AddAssetAsync(accountId, cancellationToken, "Stock");
        var archived = await client.AddAssetAsync(accountId, cancellationToken, "Archived");
        await client.ArchiveAssetAsync(archived, cancellationToken);
        var bank = await client.CreateBankAccountAsync(cancellationToken, "mBank");
        var place = await client.CreateCashPlaceAsync(cancellationToken, "Sejf");
        var deposit = await client.AddDepositAsync(cancellationToken);
        var bond = (await client.CreateFundedBondAsync(cancellationToken)).Bond;
        var metal = await client.AddMetalAsync(cancellationToken);
        var group = await client.CreateAssetGroupAsync(cancellationToken);
        await client.AssignAssetsAsync(group.Id, cancellationToken, stock);

        var response = await client.GetAsync(AssignableAssetsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var assets = (await response.Content.ReadFromJsonAsync<List<AssignableAssetView>>(cancellationToken))!.ToDictionary(a => a.AssetId);
        var stockRow = assets[stock];
        Assert.Equal((AssetClass.Stock, accountId, "XTB", null, group.Id, false), (stockRow.AssetClass, stockRow.AccountId, stockRow.AccountName, stockRow.CashPlaceName, stockRow.GroupId, stockRow.IsArchived));
        Assert.True(assets[archived].IsArchived);
        Assert.Null(assets[archived].GroupId);
        Assert.Equal((accountId, "XTB"), (assets[accountCash].AccountId, assets[accountCash].AccountName));
        Assert.Equal("mBank", assets[bank.MoneyAssetId].AccountName);
        Assert.Equal(AssetClass.BankAccount, assets[bank.MoneyAssetId].AssetClass);
        var placeRow = assets[place.AssetId()];
        Assert.Equal(("Sejf", null), (placeRow.CashPlaceName, placeRow.AccountId));
        Assert.Equal(AssetClass.Deposit, assets[deposit.AssetId].AssetClass);
        Assert.Equal(AssetClass.Bond, assets[bond.AssetId].AssetClass);
        Assert.Equal(AssetClass.PreciousMetal, assets[metal.AssetId].AssetClass);
    }
}
