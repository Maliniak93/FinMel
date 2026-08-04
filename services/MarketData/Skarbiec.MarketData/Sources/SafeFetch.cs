namespace Skarbiec.MarketData.Sources;

/// <summary>
/// Turns an unhandled exception from an <see cref="IPriceSource"/>/<see cref="IFxRateSource"/> call
/// into <see cref="PriceFetchOutcome.Error"/> instead of letting it escape — shared by
/// <see cref="PriceSyncJob"/> (T2.6) and <see cref="HistoryBackfillJob"/> (T2.7) so both jobs honor
/// the tri-state fetch contract (T2.2) the same way instead of two copies of the same try/catch
/// silently drifting apart.
/// </summary>
public static class SafeFetch
{
    public static async Task<PriceFetchResult<TValue>> RunAsync<TValue>(
        Func<Task<PriceFetchResult<TValue>>> fetch, Func<Exception, string> describeError)
    {
        try
        {
            return await fetch();
        }
        catch (Exception ex)
        {
            return PriceFetchResult<TValue>.Error(describeError(ex));
        }
    }
}
