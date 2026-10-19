using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.AddAsset;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.AccountsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.AssetGroupsApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class AssetGroupGuardTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task UnknownOrForeignGroup_Returns404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var owner = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var foreignGroupId = (await owner.CreateAssetGroupAsync(cancellationToken)).Id;
        using var client = Factory.CreateAuthenticatedClient(userId);
        var brokerageId = await client.CreateAccountAsync(cancellationToken, "XTB", AccountType.Brokerage);
        var bondsId = await client.CreateAccountAsync(cancellationToken, "Obligacje", AccountType.Bonds);
        await client.FundAccountAsync(bondsId, cancellationToken);
        var deposit = await client.WithFundingAsync(NewDepositRequest(), cancellationToken);
        var stock = new AddAssetRequest
        {
            AccountId = brokerageId,
            AssetClass = AssetClass.Stock,
            Name = "Stock",
            Currency = "PLN",
            InstrumentId = FakeInstrumentLookupClient.IdFor(AssetClass.Stock, "PLN")
        };
        var before = await SnapshotUserRowsAsync(userId, cancellationToken);

        foreach (var groupId in new[] { Guid.NewGuid(), foreignGroupId })
        {
            var responses = new[]
            {
                await client.PostAsJsonAsync(AllAssetsUri, stock with { GroupId = groupId }, cancellationToken),
                await client.PostAsJsonAsync(AllBondsUri, NewBondRequest() with { AccountId = bondsId, GroupId = groupId }, cancellationToken),
                await client.PostAsJsonAsync(AllMetalsUri, NewMetalRequest() with { GroupId = groupId }, cancellationToken),
                await client.PostAsJsonAsync(AllDepositsUri, deposit with { GroupId = groupId }, cancellationToken),
            };

            foreach (var response in responses)
            {
                await response.AssertProblemAsync(HttpStatusCode.NotFound, AssetGroupNotFoundErrorCode, cancellationToken);
            }
        }

        Assert.Equal(before, await SnapshotUserRowsAsync(userId, cancellationToken));
    }
}
