using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources.Verification;

namespace Skarbiec.MarketData.Tests.Fixtures;

/// <summary>
/// Stands in for <see cref="ITickerVerifier"/> in HTTP slice tests (M1.6) — same "swap the one
/// external-dependency boundary at the test host" pattern Portfolio's <c>FakeInstrumentLookupClient</c>
/// established for T2.9. Every (source, ticker) not explicitly scripted defaults to
/// <see cref="TickerVerificationOutcome.Exists"/>, so a test only has to opt in to the outcome it's
/// actually exercising — and records every call so a test can prove *which* source a given
/// <c>AssetClass</c> actually routed to (M1.6 AC: "a crypto id submitted as an ETF does not silently
/// succeed").
/// </summary>
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
