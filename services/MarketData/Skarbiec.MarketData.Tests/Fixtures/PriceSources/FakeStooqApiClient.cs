using Skarbiec.MarketData.Sources.Stooq;

namespace Skarbiec.MarketData.Tests.Fixtures.PriceSources;

/// <summary>
/// Canned <see cref="IStooqApiClient"/>, keyed per ticker — the same "swap the fetch, exercise the
/// real parse logic through the public interface" pattern <see cref="FakeNbpApiClient"/> established,
/// adapted for Stooq's per-ticker fetch shape (T2.4 scope): each ticker can be wired to its own
/// canned response or thrown exception, which is what makes isolation between instruments ("one bad
/// ticker must not fail the batch") directly testable against the real <see cref="StooqPriceSource"/>.
/// Also counts history-range calls, mirroring <see cref="FakeNbpApiClient.RangeRequestCount"/>.
/// </summary>
public sealed class FakeStooqApiClient : IStooqApiClient
{
    private readonly Dictionary<string, string> _responses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Exception> _exceptions = new(StringComparer.OrdinalIgnoreCase);

    public int HistoryRequestCount { get; private set; }

    public FakeStooqApiClient WithResponse(string ticker, string rawResponse)
    {
        _responses[ticker] = rawResponse;
        return this;
    }

    public FakeStooqApiClient ThrowingFor(string ticker, Exception exception)
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

        throw new InvalidOperationException($"FakeStooqApiClient: no canned response wired for ticker '{ticker}'.");
    }
}
