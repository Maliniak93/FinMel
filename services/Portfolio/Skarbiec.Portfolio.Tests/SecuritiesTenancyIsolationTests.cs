using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Securities;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class SecuritiesTenancyIsolationTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ForeignHoldings_Absent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(SavingsTodayUtc);
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var ownerPortfolioId = await owner.CreatePortfolioAsync(cancellationToken);
        var ownerAsset = await AddQuotedHoldingAsync(owner, ownerPortfolioId, cancellationToken, name: "Owner's stock", ticker: "OWN");
        await owner.RecordTransactionAsync(ownerPortfolioId, ownerAsset, TransactionType.Buy, 5m, new DateOnly(2026, 1, 5), cancellationToken);
        using var stranger = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var strangerPortfolioId = await stranger.CreatePortfolioAsync(cancellationToken);
        var strangerAsset = await AddQuotedHoldingAsync(stranger, strangerPortfolioId, cancellationToken, name: "Stranger's stock", ticker: "MINE");

        var response = await stranger.GetAsync(AllSecuritiesUri(AssetClass.Stock), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<SecuritiesResponse>(cancellationToken))!;
        Assert.Equal(strangerAsset, Assert.Single(body.Holdings).AssetId);
    }
}
