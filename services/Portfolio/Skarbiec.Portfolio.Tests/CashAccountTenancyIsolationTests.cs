using System.Net;
using System.Net.Http.Json;
using Skarbiec.Portfolio.Features.CashAccounts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class CashAccountTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ByStranger_DoesNotIncludeOwnersAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ownerPortfolioId = await owner.CreatePortfolioAsync(cancellationToken);
        await owner.AddCashAssetWithBalanceAsync(ownerPortfolioId, cancellationToken, name: "Owner's cash");
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        await stranger.AddCashAssetWithBalanceAsync(strangerPortfolioId, cancellationToken, balance: 10m, name: "Stranger's own");

        var response = await stranger.GetAsync(AllCashAccountsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CashAccountsResponse>(cancellationToken))!;
        Assert.Equal("Stranger's own", Assert.Single(body.Accounts).Name);
        Assert.Equal(10m, Assert.Single(body.Totals).Balance);
    }
}
