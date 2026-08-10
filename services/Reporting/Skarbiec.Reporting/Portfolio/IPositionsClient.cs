namespace Skarbiec.Reporting.Portfolio;

/// <summary>Narrow interface so tests can substitute a fake instead of standing up Portfolio's own Testcontainer host (mirrors Portfolio's own <c>IInstrumentLookupClient</c>, T2.9).</summary>
public interface IPositionsClient
{
    Task<IReadOnlyList<PositionForValuation>> GetAllPositionsAsync(CancellationToken cancellationToken);
}
