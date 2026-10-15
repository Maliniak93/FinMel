using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.CashPlacesApi;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

public sealed class ListTransferCandidatesEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_CashPlaces_ReturnsUnarchivedBalancesInTheCurrency()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var brokerage = await client.CreateAccountWithCashAsync(cancellationToken, balance: 1_000m, name: "XTB", depositedOn: DefaultTopUpDate);
        var sejf = await client.CreateCashPlaceAsync(cancellationToken, "Sejf", "PLN", "USD");
        await client.RecordTransactionAsync(sejf.AssetId("PLN"), TransactionType.Deposit, 5_000m, DefaultTopUpDate, cancellationToken, unitPrice: 1m);
        await client.AddArchivedCashAssetAsync(cancellationToken, name: "Skarbonka");

        var response = await client.GetAsync(CashPlaceCandidatesUri("PLN"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var candidate = Assert.Single((await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList());
        Assert.Equal(sejf.AssetId("PLN"), candidate.GetProperty("assetId").GetGuid());
        Assert.Equal("Sejf", candidate.GetProperty("name").GetString());
        Assert.Equal(5_000m, candidate.GetProperty("balance").GetDecimal());
        Assert.NotEqual(brokerage.CashAssetId, candidate.GetProperty("assetId").GetGuid());
    }

    [Fact]
    public async Task List_CashPlaces_OrdersByName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.CreateCashPlaceAsync(cancellationToken, "A cash");
        await client.CreateCashPlaceAsync(cancellationToken, "Z cash");
        await client.CreateCashPlaceAsync(cancellationToken, "B cash");

        var response = await client.GetAsync(CashPlaceCandidatesUri("PLN"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = (await response.ReadJsonAsync(cancellationToken)).EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ToList();
        Assert.Equal(["A cash", "B cash", "Z cash"], listed);
    }

    [Fact]
    public async Task List_UnsupportedCurrency_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddCashAssetWithBalanceAsync(cancellationToken);
        // Control: the same request with a supported currency is answered.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(TransferCandidatesUri("PLN", "Cash"), cancellationToken)).StatusCode);

        var response = await client.GetAsync(TransferCandidatesUri("CHF", "Cash"), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("PLN", null)]
    [InlineData(null, "Cash")]
    public async Task List_MissingParameter_ReturnsBadRequest(string? currency, string? assetClass)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        // Control: the endpoint exists and answers a complete request.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(TransferCandidatesUri("PLN", "Cash"), cancellationToken)).StatusCode);

        var response = await client.GetAsync(TransferCandidatesUri(currency, assetClass), cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();

        var response = await client.GetAsync(TransferCandidatesUri(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
