using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Http;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class MarketDataInstrumentLookupClientTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task CheckAsync_CallsInternalPath_WithoutAuthorizationHeader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var recorder = new HttpRequestRecorder();
        await using var host = Factory.WithRecordedOutboundHttp(recorder);
        var instrumentId = Guid.NewGuid();

        // An inbound request carrying a JWT is in flight, as when AddAsset validates an InstrumentId.
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        httpContextAccessor.HttpContext.Request.Headers.Authorization = $"Bearer {Factory.IssueAccessToken(Guid.NewGuid())}";
        try
        {
            // The factory swaps in a fake, so build the real typed client from the HttpClient Program.cs registered.
            var httpClient = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IInstrumentLookupClient));
            var client = new MarketDataInstrumentLookupClient(httpClient);

            var status = await client.CheckAsync(instrumentId, cancellationToken);

            Assert.Equal(InstrumentLookupStatus.Found, status);
        }
        finally
        {
            httpContextAccessor.HttpContext = null;
        }

        var request = Assert.Single(recorder.Requests);
        Assert.Multiple(
            () => Assert.Equal(HttpMethod.Get, request.Method),
            () => Assert.Equal($"/internal/instruments/{instrumentId}", request.Uri.AbsolutePath),
            () => Assert.Null(request.Authorization));
    }

    [Fact]
    public async Task CheckAsync_TargetPortHasNothingListening_ReturnsUnavailableAndDoesNotHang()
    {
        // Port 1 refuses the connection immediately instead of timing out, so this stays fast.
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
