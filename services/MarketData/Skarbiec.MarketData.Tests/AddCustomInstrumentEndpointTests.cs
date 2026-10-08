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
    public async Task Add_ValidStockTicker_RoutesToYahoo_ReturnsCreatedAsVerified()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddCustomInstrumentRequest
        {
            Ticker = "CDR.WA",
            Name = "CD Projekt",
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CustomInstrumentResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal("CDR.WA", body.Ticker);
        Assert.Equal(PriceSource.Yahoo, body.Source);
        Assert.Equal(InstrumentVerificationStatus.Verified, body.VerificationStatus);
        Assert.Equal($"{InstrumentsUri}/{body.Id}", response.Headers.Location?.OriginalString);
        Assert.Contains((PriceSource.Yahoo, "CDR.WA"), Factory.TickerVerifier.Calls);
    }

    [Fact]
    public async Task Add_ValidEtfTicker_RoutesToYahoo()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        var body = await client.AddCustomInstrumentAsync(cancellationToken, ticker: "VWCE.DE", assetClass: AssetClass.Etf);

        Assert.Equal(PriceSource.Yahoo, body.Source);
        Assert.Contains((PriceSource.Yahoo, "VWCE.DE"), Factory.TickerVerifier.Calls);
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

        var body = await client.AddCustomInstrumentAsync(cancellationToken, ticker: "solana", quoteCurrency: "USD", assetClass: AssetClass.Crypto);

        Assert.Equal(PriceSource.CoinGecko, body.Source);
        Assert.Equal(InstrumentVerificationStatus.Verified, body.VerificationStatus);
        Assert.Contains((PriceSource.CoinGecko, "solana"), Factory.TickerVerifier.Calls);
    }

    // The same ticker verifies against Yahoo but not CoinGecko, so only the class that derives Yahoo succeeds.
    [Fact]
    public async Task Add_SameTickerString_DifferentClassesRouteToDifferentProviders_CryptoDoesNotSilentlySucceedAsEtf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var stockClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        using var cryptoClient = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        const string ticker = "AMBIGUOUS.DE";
        Factory.TickerVerifier.WithOutcome(PriceSource.CoinGecko, ticker, TickerVerificationOutcome.DoesNotExist);
        // Yahoo isn't scripted for this ticker, so it keeps the fake's default (Exists).

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
        Assert.Contains((PriceSource.Yahoo, ticker), Factory.TickerVerifier.Calls);
        Assert.Contains((PriceSource.CoinGecko, ticker), Factory.TickerVerifier.Calls);
    }

    [Fact]
    public async Task Add_MakesInstrumentImmediatelyFindableBySearch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());

        await client.AddCustomInstrumentAsync(cancellationToken, ticker: "CDR.WA", name: "CD Projekt");

        var searchResponse = await client.GetAsync(SearchInstrumentsUri("CDR"), cancellationToken);
        var results = await searchResponse.Content.ReadFromJsonAsync<InstrumentSearchResponse>(cancellationToken);

        Assert.Single(results!.Results);
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
        Factory.TickerVerifier.WithOutcome(PriceSource.Yahoo, "GHOST.WA", TickerVerificationOutcome.DoesNotExist);
        var request = new AddCustomInstrumentRequest
        {
            Ticker = "GHOST.WA",
            Name = "Doesn't exist",
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("GHOST.WA", body);

        await using var db = CreateDbContext();
        Assert.False(await db.Instruments.AnyAsync(i => i.Ticker == "GHOST.WA", cancellationToken));
    }

    [Fact]
    public async Task Add_ProviderUnreachable_ReturnsServiceUnavailable_AndCreatesNoInstrumentByDefault()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        Factory.TickerVerifier.WithOutcome(PriceSource.Yahoo, "DOWN.WA", TickerVerificationOutcome.Unreachable);
        var request = new AddCustomInstrumentRequest
        {
            Ticker = "DOWN.WA",
            Name = "Provider is down",
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        await using var db = CreateDbContext();
        Assert.False(await db.Instruments.AnyAsync(i => i.Ticker == "DOWN.WA", cancellationToken));
    }

    [Fact]
    public async Task Add_ProviderUnreachableWithAllowUnverifiedOptIn_CreatesInstrumentAsUnverified()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        Factory.TickerVerifier.WithOutcome(PriceSource.Yahoo, "DOWN.WA", TickerVerificationOutcome.Unreachable);

        var body = await client.AddCustomInstrumentAsync(cancellationToken, ticker: "DOWN.WA", allowUnverified: true);

        Assert.Equal(InstrumentVerificationStatus.Unverified, body.VerificationStatus);

        await using var db = CreateDbContext();
        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == body.Id, cancellationToken);
        Assert.Equal(InstrumentVerificationStatus.Unverified, stored.VerificationStatus);
    }

    [Fact]
    public async Task Etf_CurrencyFromExchange_IdempotentRepeat()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddCustomInstrumentRequest
        {
            Ticker = "VWCE.DE",
            Name = "Vanguard FTSE All-World UCITS ETF",
            AssetClass = AssetClass.Etf,
        };

        var first = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);
        var second = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = await first.Content.ReadFromJsonAsync<CustomInstrumentResponse>(cancellationToken);
        Assert.NotNull(created);
        Assert.Equal("EUR", created.QuoteCurrency);
        Assert.Equal("Xetra", created.Exchange);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var repeated = await second.Content.ReadFromJsonAsync<CustomInstrumentResponse>(cancellationToken);
        Assert.Equal(created.Id, repeated!.Id);

        await using var db = CreateDbContext();
        Assert.Equal(1, await db.Instruments.CountAsync(i => i.Ticker == "VWCE.DE", cancellationToken));
    }

    [Fact]
    public async Task Stock_UnknownSuffix_Returns400()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = Factory.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new AddCustomInstrumentRequest { Ticker = "AAPL", Name = "Apple", AssetClass = AssetClass.Stock };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(cancellationToken);
        Assert.Equal("Validation.UnsupportedExchange", problem!.Extensions["errorCode"]?.ToString());
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
            Ticker = "CDR.WA",
            Name = "CD Projekt",
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
        var created = await client.AddCustomInstrumentAsync(cancellationToken, ticker: "NEW.WA");

        await using var db = CreateDbContext();
        var stored = await db.Instruments.AsNoTracking().SingleAsync(i => i.Id == created.Id, cancellationToken);

        Assert.Equal(InstrumentVerificationStatus.Verified, stored.VerificationStatus);
    }
}
