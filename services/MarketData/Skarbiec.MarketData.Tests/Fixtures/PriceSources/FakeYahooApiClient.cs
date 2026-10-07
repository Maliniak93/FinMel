using Skarbiec.MarketData.Sources.Yahoo;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

public sealed class FakeYahooApiClient : IYahooApiClient
{
    private readonly Dictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _exceptions = new(StringComparer.OrdinalIgnoreCase);

    public int HistoryRequestCount { get; private set; }

    public FakeYahooApiClient WithResponse(string ticker, string rawResponse)
    {
        _responses[ticker] = rawResponse;
        return this;
    }

    public FakeYahooApiClient ThrowingFor(string ticker, Exception exception)
    {
        _exceptions[ticker] = exception;
        return this;
    }

    public Task<string> GetLatestAsync(string ticker, CancellationToken cancellationToken) => Respond(ticker);

    public Task<string> GetHistoryAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        HistoryRequestCount++;
        return Respond(ticker);
    }

    private Task<string> Respond(string ticker)
    {
        if (_exceptions.TryGetValue(ticker, out var exception))
        {
            return Task.FromException<string>(exception);
        }

        if (_responses.TryGetValue(ticker, out var raw))
        {
            return Task.FromResult(raw);
        }

        throw new InvalidOperationException($"FakeYahooApiClient: no canned response wired for ticker '{ticker}'.");
    }
}
