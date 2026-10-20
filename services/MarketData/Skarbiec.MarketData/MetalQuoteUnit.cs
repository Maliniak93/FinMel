using Skarbiec.Contracts;

namespace Skarbiec.MarketData;

public static class MetalQuoteUnit
{
    public const decimal GramsPerTroyOunce = 31.1034768m;

    public static decimal ToServedPrice(AssetClass assetClass, decimal close) =>
        assetClass == AssetClass.PreciousMetal ? Math.Round(close / GramsPerTroyOunce, 8) : close;

    public static decimal? ToServedPrice(AssetClass assetClass, decimal? close) =>
        close is { } value ? ToServedPrice(assetClass, value) : null;
}
