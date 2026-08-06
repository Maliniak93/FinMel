using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.AddCustomInstrument;
using Skarbiec.MarketData.Features.SearchInstruments;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// HTTP-level behavior of Features/AddCustomInstrument (T2.8 AC). Under this host,
/// <c>Testing:DisableBackgroundJobs</c> registers <see cref="Skarbiec.MarketData.Sources.NoOpHistoryBackfillTrigger"/>
/// (no live Quartz scheduler here) — so these tests prove the request path itself: the instrument is
/// created <see cref="InstrumentVerificationStatus.Unverified"/> and immediately findable by search
/// ("usable"), returning before any backfill happens. <c>HistoryBackfillVerificationTests</c> proves
/// the other half — that the T2.7 job this handler triggers actually resolves Unverified to
/// Verified/Failed once it runs.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
public sealed class AddCustomInstrumentEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Add_ValidStooqTicker_ReturnsCreatedAsUnverified()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddCustomInstrumentRequest
        {
            Source = PriceSource.Stooq,
            Ticker = "MSFT.US",
            Name = "Microsoft Corp.",
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CustomInstrumentResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("MSFT.US", body.Ticker);
        Assert.Equal(InstrumentVerificationStatus.Unverified, body.VerificationStatus);
        Assert.Equal($"{InstrumentsUri}/{body.Id}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Add_ValidCoinGeckoId_ReturnsCreated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddCustomInstrumentRequest
        {
            Source = PriceSource.CoinGecko,
            Ticker = "solana",
            Name = "Solana",
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Crypto,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Add_MakesInstrumentImmediatelyFindableBySearch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        await client.AddCustomInstrumentAsync(cancellationToken, ticker: "MSFT.US", name: "Microsoft Corp.");

        var searchResponse = await client.GetAsync(SearchInstrumentsUri("MSFT"), cancellationToken);
        var results = await searchResponse.Content.ReadFromJsonAsync<List<InstrumentSearchResult>>(cancellationToken);

        Assert.Single(results!);
    }

    [Fact]
    public async Task Add_NbpSource_ReturnsBadRequest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddCustomInstrumentRequest
        {
            Source = PriceSource.Nbp,
            Ticker = "XYZ",
            Name = "Not a real NBP ticker",
            QuoteCurrency = "PLN",
            AssetClass = AssetClass.Other,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_DuplicateSourceAndTicker_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddCustomInstrumentAsync(cancellationToken, ticker: "DUP.US");

        var request = new AddCustomInstrumentRequest
        {
            Source = PriceSource.Stooq,
            Ticker = "DUP.US",
            Name = "Duplicate again",
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
        };
        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Add_MissingTicker_ReturnsBadRequestNotServerError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new { Source = PriceSource.Stooq, Name = "No ticker", QuoteCurrency = "USD", AssetClass = AssetClass.Stock };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();
        var request = new AddCustomInstrumentRequest
        {
            Source = PriceSource.Stooq,
            Ticker = "MSFT.US",
            Name = "Microsoft Corp.",
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Add_PersistsWithUnverifiedStatus_InDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var created = await client.AddCustomInstrumentAsync(cancellationToken, ticker: "NEW.US");

        await using var db = CreateDbContext();
        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == created.Id, cancellationToken);

        Assert.Equal(InstrumentVerificationStatus.Unverified, stored.VerificationStatus);
    }
}
