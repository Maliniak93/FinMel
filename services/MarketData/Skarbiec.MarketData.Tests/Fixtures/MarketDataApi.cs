using System.Net.Http.Json;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.AddCustomInstrument;
using Skarbiec.MarketData.Features.SearchInstruments;

namespace Skarbiec.MarketData.Tests.Fixtures;

/// <summary>
/// MarketData's HTTP surface as arrange-step helpers: route builders, plus the "add me a custom
/// instrument" call slice tests need before exercising the endpoint they actually care about.
/// </summary>
/// <remarks>
/// Arrange only. A test asserting on one of these endpoints must call it directly and assert on the
/// raw <see cref="HttpResponseMessage"/> — the helpers here <c>EnsureSuccessStatusCode</c>, which
/// would turn the very failure such a test is looking for into an exception.
/// </remarks>
internal static class MarketDataApi
{
    public const string InstrumentsUri = "/api/marketdata/instruments";
    public const string SearchInstrumentsBaseUri = "/api/marketdata/instruments/search";

    public static string InstrumentUri(Guid id) => $"{InstrumentsUri}/{id}";

    public static string SearchInstrumentsUri(string? q = null, int? limit = null)
    {
        var parameters = new List<string>();
        if (q is not null)
        {
            parameters.Add($"q={Uri.EscapeDataString(q)}");
        }

        if (limit is not null)
        {
            parameters.Add($"limit={limit}");
        }

        return parameters.Count > 0 ? $"{SearchInstrumentsBaseUri}?{string.Join('&', parameters)}" : SearchInstrumentsBaseUri;
    }

    public static async Task<CustomInstrumentResponse> AddCustomInstrumentAsync(
        this HttpClient client,
        CancellationToken cancellationToken,
        PriceSource source = PriceSource.Stooq,
        string ticker = "MSFT.US",
        string name = "Microsoft Corp.",
        string quoteCurrency = "USD",
        AssetClass assetClass = AssetClass.Stock)
    {
        var request = new AddCustomInstrumentRequest
        {
            Source = source,
            Ticker = ticker,
            Name = name,
            QuoteCurrency = quoteCurrency,
            AssetClass = assetClass,
        };

        var response = await client.PostAsJsonAsync(InstrumentsUri, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<CustomInstrumentResponse>(cancellationToken))!;
    }

    /// <summary>Seeds an instrument directly (no fetch involved) and returns its id.</summary>
    public static async Task<Guid> SeedInstrumentAsync(
        this MarketDataDbContext db,
        string ticker,
        string name,
        PriceSource source,
        string quoteCurrency,
        CancellationToken cancellationToken,
        AssetClass assetClass = AssetClass.Stock)
    {
        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Source = source,
            QuoteCurrency = quoteCurrency,
            AssetClass = assetClass,
        };
        db.Instruments.Add(instrument);
        await db.SaveChangesAsync(cancellationToken);

        return instrument.Id;
    }

    public static async Task SeedQuoteAsync(
        this MarketDataDbContext db, Guid instrumentId, DateOnly date, decimal close, CancellationToken cancellationToken)
    {
        db.PriceQuotes.Add(new PriceQuote { Id = Guid.NewGuid(), InstrumentId = instrumentId, Date = date, Close = close });
        await db.SaveChangesAsync(cancellationToken);
    }
}
