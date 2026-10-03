using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Bonds;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class ListBondsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_AcrossPortfolios_OrdersByMaturityDateThenName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(BondPurchaseDayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var firstId = await client.CreatePortfolioAsync(cancellationToken, name: "First");
        var secondId = await client.CreatePortfolioAsync(cancellationToken, name: "Second");
        var september = new DateOnly(2026, 9, 1);
        await client.AddBondAsync(firstId, cancellationToken, NewBondRequest(name: "Zeta", seriesCode: "OTS1226", type: TreasuryBondType.Ots, purchaseDate: september));
        await client.AddBondAsync(secondId, cancellationToken, NewBondRequest(name: "Edo long"));
        await client.AddBondAsync(secondId, cancellationToken, NewBondRequest(name: "Alpha", seriesCode: "OTS1226", type: TreasuryBondType.Ots, purchaseDate: september));
        await client.AddBondAsync(firstId, cancellationToken, NewBondRequest(name: "Ror", seriesCode: "ROR0627", type: TreasuryBondType.Ror, purchaseDate: new DateOnly(2026, 6, 10)));

        var response = await client.GetAsync(AllBondsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bonds = await response.Content.ReadFromJsonAsync<List<BondResponse>>(cancellationToken);
        Assert.NotNull(bonds);
        Assert.Equal(["Alpha", "Zeta", "Ror", "Edo long"], bonds.Select(b => b.Name));
        Assert.Equal(secondId, bonds[0].PortfolioId);
        Assert.Equal("Second", bonds[0].PortfolioName);
        Assert.Equal(firstId, bonds[1].PortfolioId);
        Assert.Equal("First", bonds[1].PortfolioName);
        Assert.Equal(new DateOnly(2026, 12, 1), bonds[0].MaturityDate);
    }
}
