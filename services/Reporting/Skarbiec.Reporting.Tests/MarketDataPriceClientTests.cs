using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Skarbiec.Reporting.MarketData;
using Skarbiec.Reporting.Tests.Fixtures;
using Skarbiec.Testing;
using Skarbiec.Testing.Containers;
using Skarbiec.Testing.Http;

namespace Skarbiec.Reporting.Tests;

// Resolved through Reporting's own IHttpClientFactory registration, so every handler Program.cs adds runs.
[Collection(TestingDefaults.CollectionName)]
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
}
