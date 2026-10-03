using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources.Verification;

namespace Skarbiec.MarketData.Tests.Fixtures;

// An unscripted (source, ticker) defaults to Exists, and every call is recorded to show which source a class routed to.
public sealed class FakeTickerVerifier : ITickerVerifier
{
    private readonly Dictionary<(PriceSource Source, string Ticker), TickerVerificationOutcome> _overrides = [];

    public List<(PriceSource Source, string Ticker)> Calls { get; } = [];

    public FakeTickerVerifier WithOutcome(PriceSource source, string ticker, TickerVerificationOutcome outcome)
    {
        _overrides[(source, ticker)] = outcome;
        return this;
    }

    public Task<TickerVerificationOutcome> VerifyAsync(PriceSource source, string ticker, CancellationToken cancellationToken)
    {
        Calls.Add((source, ticker));
        return Task.FromResult(_overrides.GetValueOrDefault((source, ticker), TickerVerificationOutcome.Exists));
    }
}
