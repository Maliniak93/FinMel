namespace Skarbiec.Portfolio.MarketData;

public enum InstrumentLookupStatus
{
    Found,
    NotFound,
    Unavailable,
}

public interface IInstrumentLookupClient
{
    Task<InstrumentLookupStatus> CheckAsync(Guid instrumentId, CancellationToken cancellationToken);
}
