using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Sources;

public sealed record InstrumentCandidate(string Ticker, string Name, AssetClass AssetClass, string Exchange, string QuoteCurrency);

public sealed record InstrumentSearchOutcome(IReadOnlyList<InstrumentCandidate> Candidates, bool IsUnavailable)
{
    public static InstrumentSearchOutcome Unavailable { get; } = new([], IsUnavailable: true);
}

// The only provider search a Features handler may use; ArchitectureTests confine it to SearchInstruments.
public interface IInstrumentSearchSource
{
    // Writes nothing and never retries: one attempt under its own short budget, any failure is IsUnavailable.
    Task<InstrumentSearchOutcome> SearchAsync(string query, CancellationToken cancellationToken);
}
