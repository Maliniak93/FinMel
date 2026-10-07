using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Data;

// PreciousMetal maps to GoldApi, which serves only the seeded gold and silver, so no custom metal can be added.
public static class AssetClassPriceSourceMapping
{
    public static PriceSource? Resolve(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Stock or AssetClass.Etf => PriceSource.Yahoo,
        AssetClass.Crypto => PriceSource.CoinGecko,
        AssetClass.PreciousMetal => PriceSource.GoldApi,
        AssetClass.Cash or AssetClass.Deposit or AssetClass.Savings or AssetClass.Bond or AssetClass.RealEstate or AssetClass.Other => null,
        _ => throw new ArgumentOutOfRangeException(nameof(assetClass), assetClass, "Unmapped AssetClass — add it to AssetClassPriceSourceMapping.Resolve."),
    };
}
