using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.CashAccounts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ListCashAccountsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ReturnsCashAccountsByNameWithBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var cashB = await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: 300m, name: "B wallet");
        var cashA = await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: 120.50m, currency: "EUR", name: "A wallet");
        await client.AddAssetAsync(await client.CreateAccountAsync(cancellationToken), cancellationToken, name: "Some stock");
        var cashZ = await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: 75m, name: "Z wallet");
        await client.AddSavingsAccountAsync(cancellationToken);

        var response = await client.GetAsync(AllCashAccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = (await response.Content.ReadFromJsonAsync<CashAccountsResponse>(cancellationToken))!.Accounts;
        Assert.Equal([cashA, cashB, cashZ], listed.Select(a => a.AssetId).ToArray());
        var last = listed[2];
        Assert.Equal("Z wallet", last.Name);
        Assert.Equal("PLN", last.Currency);
        Assert.Equal(75m, last.Balance);
        Assert.Equal("EUR", listed[0].Currency);
        Assert.Equal(120.50m, listed[0].Balance);
        Assert.Equal(300m, listed[1].Balance);
    }

    [Fact]
    public async Task List_ArchivedCash_Excluded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var live = await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: 100m, name: "Live cash");
        var archived = await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: 200m, name: "Archived cash");
        await client.ArchiveAssetAsync(archived, cancellationToken);
        await client.AddArchivedCashAssetAsync(cancellationToken, balance: 400m);

        var response = await client.GetAsync(AllCashAccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CashAccountsResponse>(cancellationToken))!;
        Assert.Equal(live, Assert.Single(body.Accounts).AssetId);
        var total = Assert.Single(body.Totals);
        Assert.Equal("PLN", total.Currency);
        Assert.Equal(100m, total.Balance);
    }

    [Fact]
    public async Task List_ReturnsTotalsPerCurrency()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: 100.10m, currency: "USD", name: "USD wallet");
        await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: 50.25m, currency: "EUR", name: "EUR wallet");
        await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: 1_000.10m, name: "PLN one");
        await client.AddCashAssetWithBalanceAsync(cancellationToken, balance: 200.20m, name: "PLN two");

        var response = await client.GetAsync(AllCashAccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var totals = (await response.Content.ReadFromJsonAsync<CashAccountsResponse>(cancellationToken))!.Totals;
        Assert.Equal(["PLN", "EUR", "USD"], totals.Select(t => t.Currency).ToArray());
        Assert.Equal([1_200.30m, 50.25m, 100.10m], totals.Select(t => t.Balance).ToArray());
    }

    [Fact]
    public async Task List_NoCashAccounts_ReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var accountId = await client.CreateAccountAsync(cancellationToken);
        await client.AddAssetAsync(accountId, cancellationToken);

        var response = await client.GetAsync(AllCashAccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CashAccountsResponse>(cancellationToken))!;
        Assert.Empty(body.Accounts);
        Assert.Empty(body.Totals);
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(AllCashAccountsUri, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
