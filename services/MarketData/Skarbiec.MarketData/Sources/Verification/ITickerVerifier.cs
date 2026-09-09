using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.Verification;

/// <summary>
/// The three outcomes a ticker verification can produce (ADR-018), mapped 1:1 from
/// <see cref="PriceFetchOutcome"/> so <see cref="PriceFetchResult{TValue}"/>'s existing
/// data/no-data/error vocabulary carries all the way to the caller instead of collapsing to a bool.
/// </summary>
public enum TickerVerificationOutcome
{
    /// <summary>The provider returned at least one quote for the ticker — <see cref="PriceFetchOutcome.Success"/>.</summary>
    Exists,

    /// <summary>The provider answered, but has nothing for this ticker — <see cref="PriceFetchOutcome.NoData"/>
    /// (e.g. CoinGecko's empty object for an unknown coin id).</summary>
    DoesNotExist,

    /// <summary>The provider couldn't be reached, timed out, or answered with something unusable —
    /// <see cref="PriceFetchOutcome.Error"/> (e.g. Stooq's 404/JS-challenge page). Fails closed: never
    /// treated as "doesn't exist".</summary>
    Unreachable,
}

/// <summary>
/// Verifies a user-supplied ticker exists at its provider <b>before</b> an <see cref="Instrument"/> is
/// created — MarketData's one, narrowly-scoped exception to ADR-007 (ADR-018). The only abstraction a
/// <c>Features/*</c> request handler may depend on for this; the real implementation still goes
/// through <see cref="IPriceSource"/> and stays in the <c>Sources</c> namespace, so
/// <c>ArchitectureTests.OnlySourcesNamespace_DependsOn_PriceSourceAbstractions</c> keeps passing
/// unchanged, and <c>ArchitectureTests.OnlyVerificationSlice_DependsOn_TickerVerifier</c> keeps this
/// interface itself from being consumed outside the one feature that needs it — that pair is the
/// containment ADR-018 asks for.
/// </summary>
public interface ITickerVerifier
{
    /// <summary>
    /// Never persists anything (ADR-018: "verification writes nothing to the database") and never
    /// retries a rate limit — one attempt under an internal, short request-path budget, distinct from
    /// <paramref name="cancellationToken"/> (the caller's own token still cancels immediately if it fires).
    /// </summary>
    Task<TickerVerificationOutcome> VerifyAsync(PriceSource source, string ticker, CancellationToken cancellationToken);
}
