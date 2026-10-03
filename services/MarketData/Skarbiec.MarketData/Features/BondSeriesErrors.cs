using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Features;

internal static class BondSeriesErrors
{
    public static Error NotFound(string code) =>
        new("NotFound.BondSeries", $"Bond series '{code}' was not found.");
}
