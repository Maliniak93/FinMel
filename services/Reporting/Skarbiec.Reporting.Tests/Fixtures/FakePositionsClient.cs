using Skarbiec.Reporting.Portfolio;

namespace Skarbiec.Reporting.Tests.Fixtures;

/// <summary>Stands in for <see cref="IPositionsClient"/> — the Reporting test host has no Portfolio Testcontainer to call (mirrors Portfolio's own <c>FakeInstrumentLookupClient</c>, T2.9).</summary>
public sealed class FakePositionsClient : IPositionsClient
{
    private readonly List<PositionForValuation> _positions = [];

    public FakePositionsClient WithPosition(PositionForValuation position)
    {
        _positions.Add(position);
        return this;
    }

    public Task<IReadOnlyList<PositionForValuation>> GetAllPositionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PositionForValuation>>(_positions.ToList());
}
