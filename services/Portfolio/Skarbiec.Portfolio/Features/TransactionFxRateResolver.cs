using Skarbiec.Contracts;
using Skarbiec.Portfolio.MarketData;

namespace Skarbiec.Portfolio.Features;

/// <summary>
/// Resolves the <c>Transaction.FxRateToPln</c> a transaction write freezes (ADR-026) — shared by
/// RecordTransaction, UpdateTransaction and AddAsset's initial transaction. Call it after every other
/// check has passed, so neither a write to an archived portfolio nor an invalid request asks MarketData.
/// </summary>
internal static class TransactionFxRateResolver
{
    /// <returns>
    /// <c>1</c> for a PLN asset, with no MarketData call; the latest rate on or before
    /// <paramref name="date"/>; <see langword="null"/> when MarketData has none that early (the write
    /// still succeeds); or <see cref="AssetErrors.MarketDataUnavailable"/> (503) when it can't be reached.
    /// </returns>
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
