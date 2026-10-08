using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.CoinGecko;
using Skarbiec.MarketData.Sources.Yahoo;
using Skarbiec.MarketData.Sources.Verification;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

public sealed class TickerVerifierTests(SkarbiecContainersFixture containers) : MarketDataEndpointTests(containers)
{
    [Fact]
    public async Task VerifyAsync_TickerAlreadyVerifiedInDatabase_ReturnsExists_WithoutCallingThePriceSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        db.Instruments.Add(new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL.US",
            Name = "Apple Inc.",
            Source = PriceSource.Yahoo,
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
            VerificationStatus = InstrumentVerificationStatus.Verified,
        });
        await db.SaveChangesAsync(cancellationToken);

        // No latest result scripted, so any external call throws: a Verified ticker is confirmed from the database alone.
        var source = new ScriptedPriceSource(PriceSource.Yahoo);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Yahoo, "AAPL.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Exists, outcome);
    }

    [Fact]
    public async Task VerifyAsync_UnverifiedRowAlreadyPresent_StillCallsThePriceSource()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        db.Instruments.Add(new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = "PENDING.US",
            Name = "Still resolving",
            Source = PriceSource.Yahoo,
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
            VerificationStatus = InstrumentVerificationStatus.Unverified,
        });
        await db.SaveChangesAsync(cancellationToken);

        var apiClient = new FakeYahooApiClient().WithResponse("PENDING.US", RecordedResponse.Read("yahoo-latest-happy-path.json"));
        var source = new YahooPriceSource(apiClient);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Yahoo, "PENDING.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Exists, outcome);
    }

    [Fact]
    public async Task VerifyAsync_YahooReturnsAQuote_ReturnsExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var apiClient = new FakeYahooApiClient().WithResponse("MSFT.US", RecordedResponse.Read("yahoo-latest-happy-path.json"));
        var source = new YahooPriceSource(apiClient);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Yahoo, "MSFT.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Exists, outcome);
    }

    [Fact]
    public async Task VerifyAsync_Yahoo_NotFound_IsDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var apiClient = new FakeYahooApiClient().WithResponse("GHOST.WA", RecordedResponse.Read("yahoo-not-found.json"));
        var source = new YahooPriceSource(apiClient);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Yahoo, "GHOST.WA", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.DoesNotExist, outcome);
    }

    [Fact]
    public async Task VerifyAsync_YahooUnreachable_ReturnsUnreachable_NotDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var apiClient = new FakeYahooApiClient().ThrowingFor("BLOCKED.US", new HttpRequestException("rate limited", null, System.Net.HttpStatusCode.TooManyRequests));
        var source = new YahooPriceSource(apiClient);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Yahoo, "BLOCKED.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Unreachable, outcome);
    }

    [Fact]
    public async Task VerifyAsync_YahooTransportFailure_ReturnsUnreachable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var apiClient = new FakeYahooApiClient().ThrowingFor("DOWN.US", new HttpRequestException("simulated network failure"));
        var source = new YahooPriceSource(apiClient);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Yahoo, "DOWN.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Unreachable, outcome);
    }

    [Fact]
    public async Task VerifyAsync_CoinGeckoKnownId_ReturnsExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var apiClient = new FakeCoinGeckoApiClient().WithLatestResponse(RecordedResponse.Read("coingecko-latest-happy-path.json"));
        var source = new CoinGeckoPriceSource(apiClient, NullLogger<CoinGeckoPriceSource>.Instance);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.CoinGecko, "bitcoin", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Exists, outcome);
    }

    [Fact]
    public async Task VerifyAsync_CoinGeckoUnknownId_ReturnsDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var apiClient = new FakeCoinGeckoApiClient().WithLatestResponse(RecordedResponse.Read("coingecko-latest-no-data.json"));
        var source = new CoinGeckoPriceSource(apiClient, NullLogger<CoinGeckoPriceSource>.Instance);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.CoinGecko, "not-a-real-coin-id-xyz", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.DoesNotExist, outcome);
    }

    [Fact]
    public async Task VerifyAsync_NoPriceSourceRegisteredForThatSource_ReturnsUnreachable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var verifier = new TickerVerifier(db, [], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Yahoo, "ANY.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Unreachable, outcome);
    }

    // A gate never released stands in for a source that never answers; a tiny timeout keeps the test fast.
    [Fact]
    public async Task VerifyAsync_PriceSourceExceedsTimeout_ReturnsUnreachable_DoesNotPropagateCancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var gate = new TaskCompletionSource();
        var source = new GatedPriceSource(PriceSource.Yahoo, gate, PriceFetchResult<InstrumentQuote>.Success([]));
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance, TimeSpan.FromMilliseconds(50));

        var outcome = await verifier.VerifyAsync(PriceSource.Yahoo, "SLOW.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Unreachable, outcome);
    }

    [Fact]
    public async Task VerifyAsync_CallerCancels_PropagatesCancellation()
    {
        await using var db = CreateDbContext();
        var gate = new TaskCompletionSource();
        var source = new GatedPriceSource(PriceSource.Yahoo, gate, PriceFetchResult<InstrumentQuote>.Success([]));
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance, TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => verifier.VerifyAsync(PriceSource.Yahoo, "SLOW.US", cts.Token));
    }
}
