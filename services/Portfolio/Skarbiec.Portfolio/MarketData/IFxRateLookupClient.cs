namespace Skarbiec.Portfolio.MarketData;

public enum FxRateLookupStatus
{
    Found,
    NotFound,
    Unavailable,
}

public sealed record FxRateLookupResult(FxRateLookupStatus Status, decimal? Rate)
{
    public static FxRateLookupResult NotFound { get; } = new(FxRateLookupStatus.NotFound, null);

    public static FxRateLookupResult Unavailable { get; } = new(FxRateLookupStatus.Unavailable, null);

    public static FxRateLookupResult Found(decimal rate) => new(FxRateLookupStatus.Found, rate);
}

public interface IFxRateLookupClient
{
    Task<FxRateLookupResult> GetRateAsync(string currency, DateOnly date, CancellationToken cancellationToken);
}
