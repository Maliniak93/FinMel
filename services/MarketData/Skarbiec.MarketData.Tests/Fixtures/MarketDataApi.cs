using System.Net.Http.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Features.AddCustomInstrument;
using Skarbiec.MarketData.Features.SearchInstruments;
using Skarbiec.MarketData.Sources.MfBonds;
using Skarbiec.MarketData.Tests.Fixtures.PriceSources;

namespace Skarbiec.MarketData.Tests.Fixtures;

// Arrange only: helpers EnsureSuccessStatusCode, so a test of endpoint X calls X directly and asserts on the raw response.
internal static class MarketDataApi
{
    public const string InstrumentsUri = "/api/marketdata/instruments";
    public const string SearchInstrumentsBaseUri = "/api/marketdata/instruments/search";

    public static string InstrumentUri(Guid id) => $"{InstrumentsUri}/{id}";

    public const string OpenApiDocumentUri = "/api/marketdata/openapi/v1.json";

    // Service-only endpoints: anonymous and outside /api/, so the Gateway has no route to them.
    public const string InternalLatestPricesBatchUri = "/internal/prices/latest-batch";
    public const string InternalFxRatesBatchUri = "/internal/fx/latest-batch";

    public const string InternalBondSeriesRatesBatchUri = "/internal/bond-series/rates-batch";

    public const string BondSeriesBaseUri = "/api/marketdata/bond-series";

    public static string BondSeriesUri(DateOnly? onSaleOn = null) =>
        onSaleOn is null ? BondSeriesBaseUri : $"{BondSeriesBaseUri}?onSaleOn={onSaleOn:yyyy-MM-dd}";

    public static string BondSeriesUri(string code) => $"{BondSeriesBaseUri}/{code}";

    public static string InternalInstrumentUri(Guid id) => $"/internal/instruments/{id}";

    public static string InternalFxRateUri(string currency, DateOnly date) =>
        $"/internal/fx/{currency}/rate?date={date:yyyy-MM-dd}";

    public static string SearchInstrumentsUri(string? q = null, int? limit = null, AssetClass? assetClass = null)
    {
        var parameters = new List<string>();
        if (assetClass is not null)
        {
            parameters.Add($"assetClass={assetClass}");
        }

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
        string ticker = "CDR.WA",
        string name = "CD Projekt",
        string? quoteCurrency = null,
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

    public static async Task SeedBondCatalogFromFixtureAsync(this MarketDataDbContext db, CancellationToken cancellationToken)
    {
        var job = new BondCatalogSyncJob(
            db, FakeMfBondSource.FromFixture(), TimeProvider.System, NullLogger<BondCatalogSyncJob>.Instance);
        await job.RunAsync(cancellationToken);
    }

    public static async Task SeedBondSeriesAsync(
        this MarketDataDbContext db,
        string code,
        TreasuryBondType type,
        DateOnly saleStart,
        DateOnly saleEnd,
        CancellationToken cancellationToken,
        IReadOnlyList<decimal>? periodRatesPercent = null)
    {
        db.BondSeries.Add(new BondSeries
        {
            Code = code,
            Type = type,
            Isin = $"PL{code}000",
            SaleStart = saleStart,
            SaleEnd = saleEnd,
            IssuePrice = 100m,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            PeriodRates = [.. (periodRatesPercent ?? []).Select((rate, index) => new BondSeriesPeriodRate { SeriesCode = code, PeriodIndex = index, RatePercent = rate })],
        });
        await db.SaveChangesAsync(cancellationToken);
    }

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
