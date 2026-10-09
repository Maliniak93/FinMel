using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Http;

namespace Skarbiec.Reporting.Tests;

// Resolved through Reporting's own IHttpClientFactory registration, so every handler Program.cs adds runs.
public sealed class MarketDataPriceClientTests(SkarbiecContainersFixture containers) : ReportingEndpointTests(containers)
{
    [Fact]
    public async Task Fetch_CallsInternalPaths_WithoutAuthorizationHeader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var asOfDate = new DateOnly(2026, 8, 10);
        var recorder = new HttpRequestRecorder(request => request.RequestUri!.AbsolutePath.EndsWith("/fx/latest-batch", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { Rates = new[] { new { Pair = "USDPLN", Date = new DateOnly(2026, 8, 7), Rate = 4.0m } } }),
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { Quotes = new[] { new { InstrumentId = instrumentId, QuoteCurrency = "USD", Date = new DateOnly(2026, 8, 5), Close = 160m } } }),
            });
        await using var host = Factory.WithRecordedOutboundHttp(recorder);
        await using var scope = host.Services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IPriceQuoteClient>();

        var prices = await client.GetLatestPricesAsync([instrumentId], asOfDate, cancellationToken);
        var rates = await client.GetLatestFxRatesAsync(["USDPLN"], asOfDate, cancellationToken);

        Assert.Equal(160m, prices[instrumentId].Close);
        Assert.Equal(4.0m, rates["USDPLN"].Rate);
        Assert.Multiple(
            () => Assert.Equal(
                ["/internal/prices/latest-batch", "/internal/fx/latest-batch"],
                recorder.Requests.Select(r => r.Uri.AbsolutePath)),
            () => Assert.All(recorder.Requests, r => Assert.Equal(HttpMethod.Post, r.Method)),
            () => Assert.All(recorder.Requests, r => Assert.Null(r.Authorization)));
    }

    [Fact]
    public async Task FetchHistory_CallsInternalPaths_WithoutAuthorizationHeader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var instrumentId = Guid.NewGuid();
        var from = new DateOnly(2026, 8, 1);
        var to = new DateOnly(2026, 8, 10);
        var recorder = new HttpRequestRecorder(request => request.RequestUri!.AbsolutePath.EndsWith("/fx/history-batch", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Series = new[]
                    {
                        new { Pair = "USDPLN", Rates = new[] { new { Date = new DateOnly(2026, 7, 31), Rate = 3.9m }, new { Date = new DateOnly(2026, 8, 4), Rate = 4.0m } } },
                    },
                }),
            }
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    Series = new[]
                    {
                        new { InstrumentId = instrumentId, QuoteCurrency = "USD", Quotes = new[] { new { Date = new DateOnly(2026, 7, 30), Close = 150m }, new { Date = new DateOnly(2026, 8, 5), Close = 160m } } },
                    },
                }),
            });
        await using var host = Factory.WithRecordedOutboundHttp(recorder);
        await using var scope = host.Services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<IPriceQuoteClient>();

        var prices = await client.GetPriceHistoryAsync([instrumentId], from, to, cancellationToken);
        var rates = await client.GetFxHistoryAsync(["USDPLN"], from, to, cancellationToken);

        Assert.Multiple(
            () => Assert.Equal([150m, 160m], prices[instrumentId].Select(p => p.Close)),
            () => Assert.Equal("USD", prices[instrumentId][0].QuoteCurrency),
            () => Assert.Equal([new DateOnly(2026, 7, 30), new DateOnly(2026, 8, 5)], prices[instrumentId].Select(p => p.Date)),
            () => Assert.Equal([3.9m, 4.0m], rates["USDPLN"].Select(r => r.Rate)),
            () => Assert.Equal(
                ["/internal/prices/history-batch", "/internal/fx/history-batch"],
                recorder.Requests.Select(r => r.Uri.AbsolutePath)),
            () => Assert.All(recorder.Requests, r => Assert.Equal(HttpMethod.Post, r.Method)),
            () => Assert.All(recorder.Requests, r => Assert.Null(r.Authorization)));
    }
}
