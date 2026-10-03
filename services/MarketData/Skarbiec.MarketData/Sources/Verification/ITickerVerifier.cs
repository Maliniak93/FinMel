using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.Verification;

public enum TickerVerificationOutcome
{
    Exists,

    DoesNotExist,

    // Fails closed: never treated as DoesNotExist.
    Unreachable,
}

// The only verification abstraction a Features handler may use; ArchitectureTests confine it to AddCustomInstrument.
public interface ITickerVerifier
{
    // Writes nothing and never retries a rate limit: one attempt under its own short budget.
    Task<TickerVerificationOutcome> VerifyAsync(PriceSource source, string ticker, CancellationToken cancellationToken);
}
