namespace Skarbiec.Portfolio.MarketData;

public enum InstrumentLookupStatus
{
    Found,
    NotFound,
    Unavailable,
}

/// <summary>
/// Validates an <c>Asset.InstrumentId</c> against MarketData's own dictionary — cross-service
/// consistency enforced by API, never by FK (ADR-003). Narrow interface so tests can substitute a
/// fake instead of standing up MarketData's own Testcontainer host (T2.9).
/// </summary>
public interface IInstrumentLookupClient
{
    Task<InstrumentLookupStatus> CheckAsync(Guid instrumentId, CancellationToken cancellationToken);
}
