using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.CoinGecko;
using Skarbiec.MarketData.Sources.Stooq;
using Skarbiec.MarketData.Sources.Verification;
using Skarbiec.MarketData.Tests.Fixtures;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;

namespace Skarbiec.MarketData.Tests;

/// <summary>
/// <see cref="TickerVerifier"/>'s own contract (ADR-018/M1.6): the database short-circuit for a
/// ticker already <see cref="InstrumentVerificationStatus.Verified"/>, the three-way outcome mapping
/// off real <see cref="StooqPriceSource"/>/<see cref="CoinGeckoPriceSource"/> behavior (via their
/// Fake*ApiClient — the same "exercise the real parse logic through the public interface" pattern
/// <c>StooqSourceTests</c>/<c>CoinGeckoSourceTests</c> use), and the non-retrying request-path timeout
/// budget. Exercised directly — no HTTP host, mirroring <c>HistoryBackfillJobTests</c>.
/// </summary>
[Collection(TestingDefaults.CollectionName)]
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
            Source = PriceSource.Stooq,
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
            VerificationStatus = InstrumentVerificationStatus.Verified,
        });
        await db.SaveChangesAsync(cancellationToken);

        // No latestResult scripted: FetchLatestAsync throws if it's ever called (ScriptedPriceSource's
        // own contract) — proves ADR-018's "confirmed from the database, no external call at all".
        var source = new ScriptedPriceSource(PriceSource.Stooq);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Stooq, "AAPL.US", cancellationToken);

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
            Source = PriceSource.Stooq,
            QuoteCurrency = "USD",
            AssetClass = AssetClass.Stock,
            VerificationStatus = InstrumentVerificationStatus.Unverified,
        });
        await db.SaveChangesAsync(cancellationToken);

        var apiClient = new FakeStooqApiClient().WithResponse("PENDING.US", RecordedResponse.Read("stooq-latest-happy-path.csv"));
        var source = new StooqPriceSource(apiClient);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Stooq, "PENDING.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Exists, outcome);
    }

    [Fact]
    public async Task VerifyAsync_StooqReturnsAQuote_ReturnsExists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var apiClient = new FakeStooqApiClient().WithResponse("MSFT.US", RecordedResponse.Read("stooq-latest-happy-path.csv"));
        var source = new StooqPriceSource(apiClient);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Stooq, "MSFT.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Exists, outcome);
    }

    [Fact]
    public async Task VerifyAsync_StooqNoDataMarker_ReturnsDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var apiClient = new FakeStooqApiClient().WithResponse("GHOST.US", RecordedResponse.Read("stooq-latest-no-data.csv"));
        var source = new StooqPriceSource(apiClient);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Stooq, "GHOST.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.DoesNotExist, outcome);
    }

    [Fact]
    public async Task VerifyAsync_StooqUnreachable_ReturnsUnreachable_NotDoesNotExist()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        // Recorded live from stooq.com (ADR-018): a 404 HTML page instead of the expected CSV.
        var apiClient = new FakeStooqApiClient().WithResponse("BLOCKED.US", RecordedResponse.Read("stooq-malformed.csv"));
        var source = new StooqPriceSource(apiClient);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Stooq, "BLOCKED.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Unreachable, outcome);
    }

    [Fact]
    public async Task VerifyAsync_StooqTransportFailure_ReturnsUnreachable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var apiClient = new FakeStooqApiClient().ThrowingFor("DOWN.US", new HttpRequestException("simulated network failure"));
        var source = new StooqPriceSource(apiClient);
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance);

        var outcome = await verifier.VerifyAsync(PriceSource.Stooq, "DOWN.US", cancellationToken);

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

        var outcome = await verifier.VerifyAsync(PriceSource.Stooq, "ANY.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Unreachable, outcome);
    }

    /// <summary>
    /// ADR-018's request-path budget: "one attempt, hard timeout (~5s), no rate-limit retry". A
    /// <see cref="GatedPriceSource"/> whose gate is never released simulates a source that never
    /// answers within the budget (the CoinGecko Retry-After-then-retry case this exists to cut off);
    /// a tiny injected timeout keeps the test fast instead of waiting out the real ~5s default.
    /// </summary>
    [Fact]
    public async Task VerifyAsync_PriceSourceExceedsTimeout_ReturnsUnreachable_DoesNotPropagateCancellation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var db = CreateDbContext();
        var gate = new TaskCompletionSource();
        var source = new GatedPriceSource(PriceSource.Stooq, gate, PriceFetchResult<InstrumentQuote>.Success([]));
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance, TimeSpan.FromMilliseconds(50));

        var outcome = await verifier.VerifyAsync(PriceSource.Stooq, "SLOW.US", cancellationToken);

        Assert.Equal(TickerVerificationOutcome.Unreachable, outcome);
    }

    /// <summary>The caller's own cancellation (not the internal timeout) must still propagate as a
    /// genuine cancellation rather than being swallowed into Unreachable.</summary>
    [Fact]
    public async Task VerifyAsync_CallerCancels_PropagatesCancellation()
    {
        await using var db = CreateDbContext();
        var gate = new TaskCompletionSource();
        var source = new GatedPriceSource(PriceSource.Stooq, gate, PriceFetchResult<InstrumentQuote>.Success([]));
        var verifier = new TickerVerifier(db, [source], NullLogger<TickerVerifier>.Instance, TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => verifier.VerifyAsync(PriceSource.Stooq, "SLOW.US", cts.Token));
    }
}
