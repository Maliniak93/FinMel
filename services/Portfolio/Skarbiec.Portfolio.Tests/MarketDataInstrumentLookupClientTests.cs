using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing.Auth;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Http;

namespace Skarbiec.Portfolio.Tests;

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

            await client.CheckAsync(instrumentId, cancellationToken);
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
        var result = await client.CheckAsync(Guid.NewGuid(), cancellationToken);
        stopwatch.Stop();

        Assert.Equal(InstrumentLookupStatus.Unavailable, result.Status);
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

    [Fact]
    public async Task CheckAsync_MapsBodyAndStatusCodes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var found = await CheckWithResponseAsync(
            () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { assetClass = AssetClass.Crypto, quoteCurrency = "USD" }),
            },
            cancellationToken);
        var notFound = await CheckWithResponseAsync(() => new HttpResponseMessage(HttpStatusCode.NotFound), cancellationToken);
        var serverError = await CheckWithResponseAsync(() => new HttpResponseMessage(HttpStatusCode.InternalServerError), cancellationToken);

        Assert.Equal(InstrumentLookupStatus.Found, found.Status);
        Assert.Equal(AssetClass.Crypto, found.AssetClass);
        Assert.Equal("USD", found.QuoteCurrency);
        Assert.Equal(InstrumentLookupStatus.NotFound, notFound.Status);
        Assert.Equal(InstrumentLookupStatus.Unavailable, serverError.Status);
    }

    private static Task<InstrumentLookupResult> CheckWithResponseAsync(
        Func<HttpResponseMessage> respond, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(_ => respond())) { BaseAddress = new Uri("http://marketdata.test") };
        return new MarketDataInstrumentLookupClient(httpClient).CheckAsync(Guid.NewGuid(), cancellationToken);
    }
}
