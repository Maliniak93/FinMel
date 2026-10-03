using Skarbiec.Contracts;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features;

internal static class TransactionFxRateResolver
{
    public static async Task<Result<decimal?>> ResolveFxRateToPlnAsync(
        this IFxRateLookupClient fxRateLookupClient, string currency, DateOnly date, CancellationToken cancellationToken)
    {
        if (currency == Money.BaseCurrency)
        {
            return (decimal?)1m;
        }

        var lookup = await fxRateLookupClient.GetRateAsync(currency, date, cancellationToken);

        return lookup.Status switch
        {
            FxRateLookupStatus.Found => lookup.Rate,
            FxRateLookupStatus.NotFound => (decimal?)null,
            _ => AssetErrors.MarketDataUnavailable,
        };
    }
}
