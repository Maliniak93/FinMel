using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
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
public sealed class MarketDataFxRateLookupClientTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task GetRateAsync_CallsInternalPath_WithoutAuthorizationHeaderAndReadsRate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var recorder = new HttpRequestRecorder(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { currency = "EUR", date = "2026-03-02", rate = 4.30m })
        });
        await using var host = Factory.WithRecordedOutboundHttp(recorder);

        // An inbound request carrying a JWT is in flight, as when RecordTransaction resolves the rate.
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        httpContextAccessor.HttpContext.Request.Headers.Authorization = $"Bearer {Factory.IssueAccessToken(Guid.NewGuid())}";
        FxRateLookupResult result;
        try
        {
            // The factory swaps in a fake, so build the real typed client from the HttpClient Program.cs registered.
            var httpClient = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IFxRateLookupClient));
            var client = new MarketDataFxRateLookupClient(httpClient);

            result = await client.GetRateAsync("EUR", new DateOnly(2026, 3, 4), cancellationToken);
        }
        finally
        {
            httpContextAccessor.HttpContext = null;
        }

        Assert.Equal(FxRateLookupStatus.Found, result.Status);
        Assert.Equal(4.30m, result.Rate);
        var request = Assert.Single(recorder.Requests);
        Assert.Multiple(
            () => Assert.Equal(HttpMethod.Get, request.Method),
            () => Assert.Equal("/internal/fx/EUR/rate", request.Uri.AbsolutePath),
            () => Assert.Equal("?date=2026-03-04", request.Uri.Query),
            () => Assert.Null(request.Authorization));
    }

    [Fact]
    public async Task GetRateAsync_TargetAnswersNotFound_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var recorder = new HttpRequestRecorder(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await using var host = Factory.WithRecordedOutboundHttp(recorder);
        var httpClient = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IFxRateLookupClient));
        var client = new MarketDataFxRateLookupClient(httpClient);

        var result = await client.GetRateAsync("USD", new DateOnly(2019, 1, 2), cancellationToken);

        Assert.Equal(FxRateLookupStatus.NotFound, result.Status);
        Assert.Null(result.Rate);
    }

    [Fact]
    public async Task GetRateAsync_TargetPortHasNothingListening_ReturnsUnavailable()
    {
        // Port 1 refuses the connection immediately instead of timing out, so this stays fast.
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };
        var client = new MarketDataFxRateLookupClient(httpClient);
        var cancellationToken = TestContext.Current.CancellationToken;

        var stopwatch = Stopwatch.StartNew();
        var result = await client.GetRateAsync("EUR", new DateOnly(2026, 3, 4), cancellationToken);
        stopwatch.Stop();

        Assert.Equal(FxRateLookupStatus.Unavailable, result.Status);
        Assert.Null(result.Rate);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(30), $"Expected a bounded failure, took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task GetRateAsync_CallerCancels_PropagatesCancellation()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };
        var client = new MarketDataFxRateLookupClient(httpClient);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetRateAsync("EUR", new DateOnly(2026, 3, 4), cts.Token));
    }
}
