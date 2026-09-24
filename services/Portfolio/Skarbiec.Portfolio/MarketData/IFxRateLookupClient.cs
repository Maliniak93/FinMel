namespace Skarbiec.Portfolio.MarketData;

public enum FxRateLookupStatus
{
    Found,
    NotFound,
    Unavailable,
}

/// <summary>Outcome of an FX rate lookup; <see cref="Rate"/> is set only when <see cref="Status"/> is <see cref="FxRateLookupStatus.Found"/>.</summary>
public sealed record FxRateLookupResult(FxRateLookupStatus Status, decimal? Rate)
{
    public static FxRateLookupResult NotFound { get; } = new(FxRateLookupStatus.NotFound, null);

    public static FxRateLookupResult Unavailable { get; } = new(FxRateLookupStatus.Unavailable, null);

    public static FxRateLookupResult Found(decimal rate) => new(FxRateLookupStatus.Found, rate);
}

/// <summary>
/// Resolves the <c>{currency}PLN</c> rate a transaction is frozen at — the latest one MarketData holds
/// on or before the transaction's date (ADR-026). Narrow interface so tests can substitute a fake
/// instead of standing up MarketData's own Testcontainer host, the same as <see cref="IInstrumentLookupClient"/>.
/// </summary>
public interface IFxRateLookupClient
{
    Task<FxRateLookupResult> GetRateAsync(string currency, DateOnly date, CancellationToken cancellationToken);
}
