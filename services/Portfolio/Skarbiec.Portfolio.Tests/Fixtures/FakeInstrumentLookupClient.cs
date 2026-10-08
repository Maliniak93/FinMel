using Skarbiec.Contracts;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Tests.Fixtures;

// An unscripted instrument is Found as a Stock quoted in USD, the shape most arranged assets use.
public sealed class FakeInstrumentLookupClient : IInstrumentLookupClient
{
    private readonly Dictionary<Guid, InstrumentLookupResult> _overrides = [];

    public FakeInstrumentLookupClient WithNotFound(Guid instrumentId)
    {
        _overrides[instrumentId] = InstrumentLookupResult.NotFound;
        return this;
    }

    public FakeInstrumentLookupClient WithUnavailable(Guid instrumentId)
    {
        _overrides[instrumentId] = InstrumentLookupResult.Unavailable;
        return this;
    }

    public FakeInstrumentLookupClient WithInstrument(Guid instrumentId, AssetClass assetClass, string currency)
    {
        _overrides[instrumentId] = InstrumentLookupResult.Found(assetClass, currency);
        return this;
    }

    public Task<InstrumentLookupResult> CheckAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        Task.FromResult(_overrides.GetValueOrDefault(instrumentId, InstrumentLookupResult.Found(AssetClass.Stock, "USD")));
}
