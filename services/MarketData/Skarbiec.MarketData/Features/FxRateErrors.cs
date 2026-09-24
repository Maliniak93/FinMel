using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Features;

internal static class FxRateErrors
{
    public static Error NotFound(string currency, DateOnly date) =>
        new("NotFound.FxRate", $"No {currency}PLN rate exists on or before {date:yyyy-MM-dd}.");
}
