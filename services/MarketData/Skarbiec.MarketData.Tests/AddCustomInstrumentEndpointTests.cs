using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.AddCustomInstrument;
using Skarbiec.MarketData.Features.SearchInstruments;
using Skarbiec.MarketData.Sources.Verification;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using static Skarbiec.MarketData.Tests.Fixtures.MarketDataApi;

namespace Skarbiec.MarketData.Tests;

// The outcome comes from the factory's fake verifier; TickerVerifierTests cover the real provider mapping.
[Collection(TestingDefaults.CollectionName)]
public sealed class AddCustomInstrumentEndpointTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task Add_ValidStockTicker_RoutesToStooq_ReturnsCreatedAsVerified()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddCustomInstrumentRequest
        {
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
        Assert.Equal(PriceSource.Stooq, body.Source);
        Assert.Equal(InstrumentVerificationStatus.Verified, body.VerificationStatus);
        Assert.Equal($"{InstrumentsUri}/{body.Id}", response.Headers.Location?.OriginalString);
        Assert.Contains((PriceSource.Stooq, "MSFT.US"), Factory.TickerVerifier.Calls);
    }

    [Fact]
    public async Task Add_ValidEtfTicker_RoutesToStooq()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var body = await client.AddCustomInstrumentAsync(cancellationToken, ticker: "VWCE.DE", assetClass: AssetClass.Etf);

        Assert.Equal(PriceSource.Stooq, body.Source);
        Assert.Contains((PriceSource.Stooq, "VWCE.DE"), Factory.TickerVerifier.Calls);
    }

    [Fact]
    public async Task Add_BondClass_ReturnsUnsupported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddCustomInstrumentRequest
        {
            Ticker = "BOND.PL",
            Name = "Treasury bond",
            QuoteCurrency = "PLN",
            AssetClass = AssetClass.Bond,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(cancellationToken);
        Assert.NotNull(problem);
        Assert.Equal("Validation.UnsupportedInstrumentAssetClass", problem.Extensions["errorCode"]?.ToString());
        Assert.Empty(Factory.TickerVerifier.Calls);
    }

    [Fact]
    public async Task Add_ValidCoinGeckoId_RoutesToCoinGecko_ReturnsCreatedAsVerified()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var body = await client.AddCustomInstrumentAsync(cancellationToken, ticker: "solana", assetClass: AssetClass.Crypto);

        Assert.Equal(PriceSource.CoinGecko, body.Source);
        Assert.Equal(InstrumentVerificationStatus.Verified, body.VerificationStatus);
        Assert.Contains((PriceSource.CoinGecko, "solana"), Factory.TickerVerifier.Calls);
    }

    // The same ticker verifies against Stooq but not CoinGecko, so only the class that derives Stooq succeeds.
    [Fact]
    public async Task Add_SameTickerString_DifferentClassesRouteToDifferentProviders_CryptoDoesNotSilentlySucceedAsEtf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var stockClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var cryptoClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        const string ticker = "AMBIGUOUS";
        Factory.TickerVerifier.WithOutcome(PriceSource.CoinGecko, ticker, TickerVerificationOutcome.DoesNotExist);
        // Stooq isn't scripted for this ticker, so it keeps the fake's default (Exists).

        var etfResponse = await stockClient.PostAsJsonAsync(
            InstrumentsUri,
            new AddCustomInstrumentRequest { Ticker = ticker, Name = "Ambiguous as ETF", QuoteCurrency = "USD", AssetClass = AssetClass.Etf },
            cancellationToken);
        var cryptoResponse = await cryptoClient.PostAsJsonAsync(
            InstrumentsUri,
            new AddCustomInstrumentRequest { Ticker = ticker, Name = "Ambiguous as crypto", QuoteCurrency = "USD", AssetClass = AssetClass.Crypto },
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, etfResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, cryptoResponse.StatusCode);
        Assert.Contains((PriceSource.Stooq, ticker), Factory.TickerVerifier.Calls);
        Assert.Contains((PriceSource.CoinGecko, ticker), Factory.TickerVerifier.Calls);
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
    public async Task Add_PreciousMetalClass_ReturnsBadRequest_MetalsAreSeededOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddCustomInstrumentRequest
        {
            Ticker = "XAG",
            Name = "Not the seeded gold ticker",
            QuoteCurrency = "PLN",
            AssetClass = AssetClass.PreciousMetal,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("PreciousMetal", body);
        Assert.DoesNotContain("NBP", body);
    }

    [Theory]
    [InlineData(AssetClass.Cash)]
    [InlineData(AssetClass.Deposit)]
    [InlineData(AssetClass.RealEstate)]
    [InlineData(AssetClass.Other)]
    public async Task Add_AssetClassWithNoMarketProvider_ReturnsBadRequest(AssetClass assetClass)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddCustomInstrumentRequest
        {
            Ticker = "N/A",
            Name = "No market provider for this class",
            QuoteCurrency = "PLN",
            AssetClass = assetClass,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Factory.TickerVerifier.Calls);
    }

    [Fact]
    public async Task Add_TickerDoesNotExistAtProvider_ReturnsBadRequestNamingTicker_AndCreatesNoInstrument()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        Factory.TickerVerifier.WithOutcome(PriceSource.Stooq, "GHOST.US", TickerVerificationOutcome.DoesNotExist);
        var request = new AddCustomInstrumentRequest
        {
            Ticker = "GHOST.US",
            Name = "Doesn't exist",
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("GHOST.US", body);

        await using var db = CreateDbContext();
        Assert.False(await db.Instruments.AnyAsync(i => i.Ticker == "GHOST.US", cancellationToken));
    }

    [Fact]
    public async Task Add_ProviderUnreachable_ReturnsServiceUnavailable_AndCreatesNoInstrumentByDefault()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        Factory.TickerVerifier.WithOutcome(PriceSource.Stooq, "DOWN.US", TickerVerificationOutcome.Unreachable);
        var request = new AddCustomInstrumentRequest
        {
            Ticker = "DOWN.US",
            Name = "Provider is down",
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        await using var db = CreateDbContext();
        Assert.False(await db.Instruments.AnyAsync(i => i.Ticker == "DOWN.US", cancellationToken));
    }

    [Fact]
    public async Task Add_ProviderUnreachableWithAllowUnverifiedOptIn_CreatesInstrumentAsUnverified()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        Factory.TickerVerifier.WithOutcome(PriceSource.Stooq, "DOWN.US", TickerVerificationOutcome.Unreachable);

        var body = await client.AddCustomInstrumentAsync(cancellationToken, ticker: "DOWN.US", allowUnverified: true);

        Assert.Equal(InstrumentVerificationStatus.Unverified, body.VerificationStatus);

        await using var db = CreateDbContext();
        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == body.Id, cancellationToken);
        Assert.Equal(InstrumentVerificationStatus.Unverified, stored.VerificationStatus);
    }

    [Fact]
    public async Task Add_DuplicateTicker_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        await client.AddCustomInstrumentAsync(cancellationToken, ticker: "DUP.US");

        var request = new AddCustomInstrumentRequest
        {
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
        var request = new { Name = "No ticker", QuoteCurrency = "USD", AssetClass = AssetClass.Stock };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_WithoutToken_ReturnsUnauthorized()
    {
        using var client = Factory.CreateClient();
        var request = new AddCustomInstrumentRequest
        {
            Ticker = "MSFT.US",
            Name = "Microsoft Corp.",
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Add_PersistsWithVerifiedStatus_InDatabase()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var created = await client.AddCustomInstrumentAsync(cancellationToken, ticker: "NEW.US");

        await using var db = CreateDbContext();
        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == created.Id, cancellationToken);

        Assert.Equal(InstrumentVerificationStatus.Verified, stored.VerificationStatus);
    }
}
