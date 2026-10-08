using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Sources.Yahoo;

// Keeps only the configured exchanges and the EQUITY/ETF quote types; anything else Yahoo lists is dropped.
public sealed class YahooInstrumentSearch : IInstrumentSearchSource
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly IYahooApiClient _client;
    private readonly InstrumentSearchOptions _options;
    private readonly ILogger<YahooInstrumentSearch> _logger;
    private readonly TimeSpan _timeout;

    public YahooInstrumentSearch(IYahooApiClient client, IOptions<InstrumentSearchOptions> options, ILogger<YahooInstrumentSearch> logger)
        : this(client, options, logger, DefaultTimeout)
    {
    }

    // Test seam: a test proves the timeout budget without waiting it out.
    public YahooInstrumentSearch(
        IYahooApiClient client, IOptions<InstrumentSearchOptions> options, ILogger<YahooInstrumentSearch> logger, TimeSpan timeout)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
        _timeout = timeout;
    }

    public async Task<InstrumentSearchOutcome> SearchAsync(string query, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        string raw;
        try
        {
            raw = await _client.SearchAsync(query, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("YahooInstrumentSearch: search for '{Query}' exceeded the {Timeout} budget.", query, _timeout);
            return InstrumentSearchOutcome.Unavailable;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "YahooInstrumentSearch: search for '{Query}' failed.", query);
            return InstrumentSearchOutcome.Unavailable;
        }

        SearchEnvelopeDto? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<SearchEnvelopeDto>(raw);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "YahooInstrumentSearch: unreadable search response for '{Query}'.", query);
            return InstrumentSearchOutcome.Unavailable;
        }

        var candidates = new List<InstrumentCandidate>();
        foreach (var quote in envelope?.Quotes ?? [])
        {
            if (ToCandidate(quote) is { } candidate)
            {
                candidates.Add(candidate);
            }
        }

        return new InstrumentSearchOutcome(candidates, IsUnavailable: false);
    }

    private InstrumentCandidate? ToCandidate(QuoteDto quote)
    {
        AssetClass? assetClass = quote.QuoteType switch
        {
            "EQUITY" => AssetClass.Stock,
            "ETF" => AssetClass.Etf,
            _ => null,
        };
        var exchange = _options.FindByProviderCode(quote.Exchange);
        var name = quote.LongName ?? quote.ShortName;

        if (assetClass is null || exchange is null || string.IsNullOrWhiteSpace(quote.Symbol) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new InstrumentCandidate(quote.Symbol, name, assetClass.Value, exchange.Name, exchange.Currency.ToUpperInvariant());
    }

    private sealed record SearchEnvelopeDto([property: JsonPropertyName("quotes")] List<QuoteDto>? Quotes);

    private sealed record QuoteDto(
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("shortname")] string? ShortName,
        [property: JsonPropertyName("longname")] string? LongName,
        [property: JsonPropertyName("exchange")] string? Exchange,
        [property: JsonPropertyName("quoteType")] string? QuoteType);
}
