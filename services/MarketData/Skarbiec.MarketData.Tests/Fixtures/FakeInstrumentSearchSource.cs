using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Tests.Fixtures;

// Unscripted, it answers with no candidates; every query is recorded to show whether the provider was asked.
public sealed class FakeInstrumentSearchSource : IInstrumentSearchSource
{
    private InstrumentSearchOutcome _outcome = new([], IsUnavailable: false);

    public List<string> Calls { get; } = [];

    public FakeInstrumentSearchSource WithResults(params InstrumentCandidate[] candidates)
    {
        _outcome = new InstrumentSearchOutcome(candidates, IsUnavailable: false);
        return this;
    }

    public FakeInstrumentSearchSource WithUnavailable()
    {
        _outcome = new InstrumentSearchOutcome([], IsUnavailable: true);
        return this;
    }

    public Task<InstrumentSearchOutcome> SearchAsync(string query, CancellationToken cancellationToken)
    {
        Calls.Add(query);
        return Task.FromResult(_outcome);
    }
}
