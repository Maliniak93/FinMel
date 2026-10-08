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
    public async Task List_AcrossPortfolios_OrdersByPortfolioThenName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Factory.Clock.SetUtcNow(MetalTodayUtc);
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var vault = await client.CreatePortfolioAsync(cancellationToken, name: "Vault");
        var home = await client.CreatePortfolioAsync(cancellationToken, name: "Home");
        await client.AddMetalAsync(vault, cancellationToken, NewMetalRequest(name: "Zebra bar", metal: Metal.Gold, fineWeight: 50m, weightUnit: WeightUnit.Gram, pieces: 2m));
        await client.AddMetalAsync(vault, cancellationToken, NewMetalRequest(name: "Alpha coin"));
        await client.AddMetalAsync(home, cancellationToken, NewMetalRequest(name: "Mid coin", withFirstPurchase: false));

        var response = await client.GetAsync(AllMetalsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = (await response.Content.ReadFromJsonAsync<List<MetalResponse>>(cancellationToken))!;
        Assert.Equal(["Mid coin", "Alpha coin", "Zebra bar"], rows.Select(r => r.Name));
        Assert.Equal(["Home", "Vault", "Vault"], rows.Select(r => r.PortfolioName));
        var zebra = rows[2];
        Assert.Equal(home, rows[0].PortfolioId);
        Assert.Equal(vault, zebra.PortfolioId);
        Assert.Equal(Metal.Gold, zebra.Metal);
        Assert.Equal(50m, zebra.FineWeightGramsPerPiece);
        Assert.Equal(2m, zebra.Pieces);
        Assert.Equal(100m, zebra.TotalFineGrams);
        Assert.False(zebra.IsArchived);
        Assert.Equal(0m, rows[0].Pieces);
        Assert.Equal(0m, rows[0].TotalFineGrams);
    }

    [Fact]
    public async Task List_WithoutHoldings_ReturnsEmpty()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreatePortfolioAsync(cancellationToken);

        var response = await client.GetAsync(AllMetalsUri, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<List<MetalResponse>>(cancellationToken))!);
    }
}
