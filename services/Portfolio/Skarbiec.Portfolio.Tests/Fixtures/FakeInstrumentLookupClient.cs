using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Tests.Fixtures;

public sealed class FakeInstrumentLookupClient : IInstrumentLookupClient
{
    private readonly Dictionary<Guid, InstrumentLookupStatus> _overrides = [];

    public FakeInstrumentLookupClient WithNotFound(Guid instrumentId)
    {
        _overrides[instrumentId] = InstrumentLookupStatus.NotFound;
        return this;
    }

    public FakeInstrumentLookupClient WithUnavailable(Guid instrumentId)
    {
        _overrides[instrumentId] = InstrumentLookupStatus.Unavailable;
        return this;
    }

    public Task<InstrumentLookupStatus> CheckAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        Task.FromResult(_overrides.GetValueOrDefault(instrumentId, InstrumentLookupStatus.Found));
}
