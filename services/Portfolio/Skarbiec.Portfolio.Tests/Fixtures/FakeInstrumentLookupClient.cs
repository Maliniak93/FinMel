using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Tests.Fixtures;

/// <summary>
/// Stands in for <see cref="IInstrumentLookupClient"/> in HTTP slice tests — the Portfolio test host
/// has no MarketData Testcontainer to call, so AddAsset/UpdateAsset's "validate via MarketData" step
/// is substituted here instead (T2.9). Every id defaults to <see cref="InstrumentLookupStatus.Found"/>
/// so a test only has to opt in to the outcome it's actually exercising.
/// </summary>
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
