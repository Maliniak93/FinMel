using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Skarbiec.Portfolio.MarketData;
using Skarbiec.Testing.Http;

namespace Skarbiec.Portfolio.Tests;

public sealed class MarketDataInstrumentQuoteLookupClientTests
{
    private static readonly DateOnly AsOfDate = new(2026, 2, 1);

    [Fact]
    public async Task GetQuotesAsync_CallsInstrumentsBatchThenFxBatch_WithoutAuthorization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var euroId = Guid.NewGuid();
        var zlotyId = Guid.NewGuid();
        var bodies = new List<string>();
        var recorder = new HttpRequestRecorder(request =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());

            return request.RequestUri!.AbsolutePath switch
            {
                "/internal/instruments/batch" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        instruments = new object[]
                        {
                            new { instrumentId = euroId, ticker = "VWCE", name = "Vanguard FTSE All-World", exchange = "XETRA", quoteCurrency = "EUR", lastPrice = 120m, lastPriceDate = "2026-01-30" },
                            new { instrumentId = zlotyId, ticker = "CDR", name = "CD Projekt", exchange = (string?)null, quoteCurrency = "PLN", lastPrice = (decimal?)null, lastPriceDate = (string?)null }
                        }
                    })
                },
                "/internal/fx/latest-batch" => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { rates = new[] { new { pair = "EURPLN", date = "2026-01-30", rate = 4.30m } } })
                },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });
        using var httpClient = NewHttpClient(recorder);
        var client = new MarketDataInstrumentQuoteLookupClient(httpClient);

        var result = await client.GetQuotesAsync([euroId, zlotyId], AsOfDate, cancellationToken);

        Assert.Equal(InstrumentQuoteLookupStatus.Found, result.Status);
        var euro = result.Instruments![euroId];
        Assert.Multiple(
            () => Assert.Equal("VWCE", euro.Ticker),
            () => Assert.Equal("XETRA", euro.Exchange),
            () => Assert.Equal("EUR", euro.QuoteCurrency),
            () => Assert.Equal(120m, euro.LastPrice),
            () => Assert.Equal(new DateOnly(2026, 1, 30), euro.LastPriceDate),
            () => Assert.Null(result.Instruments[zlotyId].LastPrice),
            () => Assert.Equal(4.30m, result.Rates!["EUR"]));
        Assert.Equal(2, recorder.Requests.Count);
        Assert.All(recorder.Requests, request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Null(request.Authorization);
        });
        Assert.Equal("/internal/instruments/batch", recorder.Requests[0].Uri.AbsolutePath);
        Assert.Equal("/internal/fx/latest-batch", recorder.Requests[1].Uri.AbsolutePath);
        using var fxBody = JsonDocument.Parse(bodies[1]);
        Assert.Equal(["EURPLN"], fxBody.RootElement.GetProperty("pairs").EnumerateArray().Select(p => p.GetString()!).ToArray());
        Assert.Equal("2026-02-01", fxBody.RootElement.GetProperty("asOfDate").GetString());
    }

    [Fact]
    public async Task GetQuotesAsync_OnlyPlnInstruments_MakesNoFxCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var zlotyId = Guid.NewGuid();
        var recorder = new HttpRequestRecorder(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                instruments = new[]
                {
                    new { instrumentId = zlotyId, ticker = "CDR", name = "CD Projekt", exchange = "GPW", quoteCurrency = "PLN", lastPrice = (decimal?)200m, lastPriceDate = (string?)"2026-01-30" }
                }
            })
        });
        using var httpClient = NewHttpClient(recorder);
        var client = new MarketDataInstrumentQuoteLookupClient(httpClient);

        var result = await client.GetQuotesAsync([zlotyId], AsOfDate, cancellationToken);

        Assert.Equal(InstrumentQuoteLookupStatus.Found, result.Status);
        var request = Assert.Single(recorder.Requests);
        Assert.Equal("/internal/instruments/batch", request.Uri.AbsolutePath);
    }

    [Fact]
    public async Task GetQuotesAsync_TargetAnswers500_ReturnsUnavailable()
    {
        var recorder = new HttpRequestRecorder(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var httpClient = NewHttpClient(recorder);
        var client = new MarketDataInstrumentQuoteLookupClient(httpClient);

        var result = await client.GetQuotesAsync([Guid.NewGuid()], AsOfDate, TestContext.Current.CancellationToken);

        Assert.Equal(InstrumentQuoteLookupStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task GetQuotesAsync_TargetPortHasNothingListening_ReturnsUnavailable()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };
        var client = new MarketDataInstrumentQuoteLookupClient(httpClient);

        var result = await client.GetQuotesAsync([Guid.NewGuid()], AsOfDate, TestContext.Current.CancellationToken);

        Assert.Equal(InstrumentQuoteLookupStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task GetQuotesAsync_CallerCancels_PropagatesCancellation()
    {
        using var httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1") };
        var client = new MarketDataInstrumentQuoteLookupClient(httpClient);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetQuotesAsync([Guid.NewGuid()], AsOfDate, cts.Token));
    }

    private static HttpClient NewHttpClient(HttpRequestRecorder recorder) =>
        new(recorder.CreateHandler()) { BaseAddress = new Uri("http://marketdata.test") };
}
