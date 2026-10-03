namespace Skarbiec.MarketData.Sources;

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
