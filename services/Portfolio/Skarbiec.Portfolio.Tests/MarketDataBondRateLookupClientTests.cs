using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Portfolio.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Http;

namespace Skarbiec.Portfolio.Tests;

[Collection(TestingDefaults.CollectionName)]
public sealed class MarketDataBondRateLookupClientTests(SkarbiecContainersFixture containers) : PortfolioEndpointTests(containers)
{
    [Fact]
    public async Task GetRatesAsync_CallsInternalPathWithoutAuthorization_ReadsRatesByOneBasedPeriod()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var recorder = new HttpRequestRecorder(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                series = new[]
                {
                    new
                    {
                        code = "EDO1036",
                        periodRates = new[] { new { periodIndex = 0, ratePercent = 5.35m }, new { periodIndex = 1, ratePercent = 4.00m } }
                    }
                }
            })
        });
        await using var host = Factory.WithRecordedOutboundHttp(recorder);
        var httpClient = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IBondRateLookupClient));
        var client = new MarketDataBondRateLookupClient(httpClient);

        var result = await client.GetRatesAsync(["EDO1036"], cancellationToken);

        Assert.Equal(BondRateLookupStatus.Found, result.Status);
        var rates = result.Rates!["EDO1036"];
        Assert.Equal(5.35m, rates[1]);
        Assert.Equal(4.00m, rates[2]);
        var request = Assert.Single(recorder.Requests);
        Assert.Multiple(
            () => Assert.Equal(HttpMethod.Post, request.Method),
            () => Assert.Equal("/internal/bond-series/rates-batch", request.Uri.AbsolutePath),
            () => Assert.Null(request.Authorization));
    }

    [Fact]
    public async Task GetRatesAsync_TargetAnswers500_ReturnsUnavailable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var recorder = new HttpRequestRecorder(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await using var host = Factory.WithRecordedOutboundHttp(recorder);
        var httpClient = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(IBondRateLookupClient));
        var client = new MarketDataBondRateLookupClient(httpClient);

        var result = await client.GetRatesAsync(["EDO1036"], cancellationToken);

        Assert.Equal(BondRateLookupStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task GetRatesAsync_TargetPortHasNothingListening_ReturnsUnavailable()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };
        var client = new MarketDataBondRateLookupClient(httpClient);

        var result = await client.GetRatesAsync(["EDO1036"], TestContext.Current.CancellationToken);

        Assert.Equal(BondRateLookupStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task GetRatesAsync_CallerCancels_PropagatesCancellation()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };
        var client = new MarketDataBondRateLookupClient(httpClient);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetRatesAsync(["EDO1036"], cts.Token));
    }
}
