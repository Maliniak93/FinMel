using System.Net;
using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Metals;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ListMetalsEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_OrdersByName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddMetalAsync(cancellationToken, NewMetalRequest(name: "Zebra bar", metal: Metal.Gold, fineWeight: 50m, weightUnit: WeightUnit.Gram, pieces: 2m));
        await client.AddMetalAsync(cancellationToken, NewMetalRequest(name: "Alpha coin"));
        await client.AddMetalAsync(cancellationToken, NewMetalRequest(name: "Mid coin", withFirstPurchase: false));

        var response = await client.GetAsync(AllMetalsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = (await response.Content.ReadFromJsonAsync<List<MetalResponse>>(cancellationToken))!;
        Assert.Equal(["Alpha coin", "Mid coin", "Zebra bar"], rows.Select(r => r.Name));
        var zebra = rows[2];
        Assert.Equal(Metal.Gold, zebra.Metal);
        Assert.Equal(50m, zebra.FineWeightGramsPerPiece);
        Assert.Equal(2m, zebra.Pieces);
        Assert.Equal(100m, zebra.TotalFineGrams);
        Assert.False(zebra.IsArchived);
        Assert.Equal(0m, rows[1].Pieces);
        Assert.Equal(0m, rows[1].TotalFineGrams);
    }

    [Fact]
    public async Task List_WithoutHoldings_ReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateAccountAsync(cancellationToken);

        var response = await client.GetAsync(AllMetalsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<MetalResponse>>(cancellationToken))!);
    }
}
