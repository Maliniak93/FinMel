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

    /// <summary>MarketData's own OpenAPI document (Development only) — the TS client generator's input.</summary>
    public const string OpenApiDocumentUri = "/api/marketdata/openapi/v1.json";

    // Service-only endpoints (ADR-027): anonymous, outside /api/, so the Gateway has no route to them.
    public const string InternalLatestPricesBatchUri = "/internal/prices/latest-batch";
    public const string InternalFxRatesBatchUri = "/internal/fx/latest-batch";

    public static string InternalInstrumentUri(Guid id) => $"/internal/instruments/{id}";

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
        string ticker = "MSFT.US",
        string name = "Microsoft Corp.",
        string quoteCurrency = "USD",
        AssetClass assetClass = AssetClass.Stock,
        bool allowUnverified = false)
    {
        var request = new AddCustomInstrumentRequest
        {
            Ticker = ticker,
            Name = name,
            QuoteCurrency = quoteCurrency,
            AssetClass = assetClass,
            AllowUnverified = allowUnverified,
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

    public static async Task SeedFxRateAsync(
        this MarketDataDbContext db, string pair, DateOnly date, decimal rate, CancellationToken cancellationToken)
    {
        db.FxRates.Add(new FxRate { Id = Guid.NewGuid(), Pair = pair, Date = date, Rate = rate });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Seeds the spec-04 currency catalog (PLN + every FxSyncJob-covered non-PLN currency)
    /// directly, without going through <c>MarketDataSeeder</c> — used by tests that need the catalog
    /// present but don't care about the rest of the seeder's starter dictionary.</summary>
    public static async Task SeedCurrencyCatalogAsync(this MarketDataDbContext db, CancellationToken cancellationToken)
    {
        db.Currencies.AddRange(
            new Currency { Code = "PLN", Name = "Polish Zloty", Symbol = "zl", DecimalPlaces = 2, DisplayOrder = 0 },
            new Currency { Code = "EUR", Name = "Euro", Symbol = "EUR", DecimalPlaces = 2, DisplayOrder = 1 },
            new Currency { Code = "USD", Name = "US Dollar", Symbol = "$", DecimalPlaces = 2, DisplayOrder = 2 },
            new Currency { Code = "GBP", Name = "British Pound", Symbol = "GBP", DecimalPlaces = 2, DisplayOrder = 3 },
            new Currency { Code = "CHF", Name = "Swiss Franc", Symbol = "CHF", DecimalPlaces = 2, DisplayOrder = 4 });
        await db.SaveChangesAsync(cancellationToken);
    }
}
