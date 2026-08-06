using System.Diagnostics;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// Exercises <see cref="MarketDataInstrumentLookupClient"/> directly against a real (but
/// unreachable) <see cref="HttpClient"/> — no Testcontainers/WebApplicationFactory involved, since
/// the point is the client's own exception handling, not the HTTP pipeline around it. Proves T2.9's
/// AC ("MarketData unavailable → 503, not a hang") one layer below the endpoint tests, which instead
/// substitute <see cref="Fixtures.FakeInstrumentLookupClient"/> and never touch this class.
/// </summary>
public sealed class MarketDataInstrumentLookupClientTests
{
    [Fact]
    public async Task CheckAsync_TargetPortHasNothingListening_ReturnsUnavailableAndDoesNotHang()
    {
        // Port 1 is a reserved/unassigned TCP port unlikely to have anything listening in any test
        // environment; connection is refused immediately rather than timing out, so this stays fast.
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };
        var client = new MarketDataInstrumentLookupClient(httpClient);
        var cancellationToken = TestContext.Current.CancellationToken;

        var stopwatch = Stopwatch.StartNew();
        var status = await client.CheckAsync(Guid.NewGuid(), cancellationToken);
        stopwatch.Stop();

        Assert.Equal(InstrumentLookupStatus.Unavailable, status);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Expected a bounded failure, took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task CheckAsync_CallerCancels_PropagatesCancellation()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };
        var client = new MarketDataInstrumentLookupClient(httpClient);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CheckAsync(Guid.NewGuid(), cts.Token));
    }
}
