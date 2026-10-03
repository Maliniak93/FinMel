using System.Net;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.Portfolio.Tests.Fixtures.PortfolioApi;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class ListTransferCandidatesEndpointTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task List_ReturnsSameCurrencyClassAssetsInActivePortfolios()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var cashId = await client.AddCashAssetWithBalanceAsync(walletId, cancellationToken, balance: 5_000m);
        await client.AddCashAssetWithBalanceAsync(walletId, cancellationToken, currency: "EUR", name: "EUR cash");
        await client.AddAssetAsync(walletId, cancellationToken, name: "PLN shares", assetClass: AssetClass.Stock);
        await client.AddDepositAsync(walletId, cancellationToken, NewDepositRequest(name: "PLN deposit"));
        await client.AddArchivedCashAssetAsync(cancellationToken);

        var response = await client.GetAsync(TransferCandidatesUri("PLN", "Cash"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var candidates = (await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList();
        var candidate = Assert.Single(candidates);
        Assert.Equal(cashId, candidate.GetProperty("assetId").GetGuid());
        Assert.Equal("Cash account", candidate.GetProperty("name").GetString());
        Assert.Equal(walletId, candidate.GetProperty("portfolioId").GetGuid());
        Assert.Equal("Wallet", candidate.GetProperty("portfolioName").GetString());
        Assert.Equal(5_000m, candidate.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task List_ExcludesArchivedAssets()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        var liveId = await client.AddCashAssetWithBalanceAsync(walletId, cancellationToken, name: "Live cash");
        var archivedId = await client.AddCashAssetWithBalanceAsync(walletId, cancellationToken, name: "Archived cash");
        await client.ArchiveAssetAsync(walletId, archivedId, cancellationToken);

        var response = await client.GetAsync(TransferCandidatesUri("PLN", "Cash"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var candidate = Assert.Single((await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList());
        Assert.Equal(liveId, candidate.GetProperty("assetId").GetGuid());
    }

    [Fact]
    public async Task List_AfterFundingTransfer_ReturnsRemainingBalance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var funded = await client.CreateFundedDepositAsync(cancellationToken);

        var response = await client.GetAsync(TransferCandidatesUri("PLN", "Cash"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var candidate = Assert.Single((await response.ReadJsonAsync(cancellationToken)).EnumerateArray().ToList());
        Assert.Equal(funded.CashAssetId, candidate.GetProperty("assetId").GetGuid());
        Assert.Equal(4_000m, candidate.GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task List_OrdersByPortfolioNameThenAssetName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var zuluId = await client.CreatePortfolioAsync(cancellationToken, name: "Zulu");
        var alphaId = await client.CreatePortfolioAsync(cancellationToken, name: "Alpha");
        await client.AddCashAssetAsync(zuluId, cancellationToken, name: "A cash");
        await client.AddCashAssetAsync(alphaId, cancellationToken, name: "Z cash");
        await client.AddCashAssetAsync(alphaId, cancellationToken, name: "B cash");

        var response = await client.GetAsync(TransferCandidatesUri("PLN", "Cash"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var listed = (await response.ReadJsonAsync(cancellationToken)).EnumerateArray()
            .Select(c => $"{c.GetProperty("portfolioName").GetString()}/{c.GetProperty("name").GetString()}")
            .ToList();
        Assert.Equal(["Alpha/B cash", "Alpha/Z cash", "Zulu/A cash"], listed);
    }

    [Fact]
    public async Task List_UnsupportedCurrency_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var walletId = await client.CreatePortfolioAsync(cancellationToken, name: "Wallet");
        await client.AddCashAssetWithBalanceAsync(walletId, cancellationToken);
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
