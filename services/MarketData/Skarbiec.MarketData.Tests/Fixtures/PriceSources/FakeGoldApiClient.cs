using System.Globalization;
using Skarbiec.MarketData.Sources.GoldApi;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

// A symbol with no canned body answers null, gold-api.com's 404 for an unknown symbol.
public sealed class FakeGoldApiClient : IGoldApiClient
{
    private readonly Dictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);
    private Exception? _throwOnRequest;

    public int RequestCount { get; private set; }

    public List<string> RequestedSymbols { get; } = [];

    public FakeGoldApiClient WithResponse(string symbol, string rawResponse)
    {
        _responses[symbol] = rawResponse;
        return this;
    }

    public FakeGoldApiClient WithPricePerOunce(string symbol, decimal pricePerOunce, DateTimeOffset updatedAt) =>
        WithResponse(symbol, string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"currency":"USD","price":{{pricePerOunce}},"symbol":"{{symbol}}","updatedAt":"{{updatedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}"}"""));

    public FakeGoldApiClient ThrowingOnRequest(Exception exception)
    {
        _throwOnRequest = exception;
        return this;
    }

    public Task<string?> GetPriceAsync(string symbol, CancellationToken cancellationToken)
    {
        RequestCount++;
        RequestedSymbols.Add(symbol);

        if (_throwOnRequest is not null)
        {
            return Task.FromException<string?>(_throwOnRequest);
        }

        return Task.FromResult(_responses.TryGetValue(symbol, out var raw) ? raw : null);
    }
}
