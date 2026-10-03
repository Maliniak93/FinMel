using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Data;

// PreciousMetal maps to NBP, which verifies no custom ticker, so gold is picked from the seeded dictionary instead.
public static class AssetClassPriceSourceMapping
{
    public static PriceSource? Resolve(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Stock or AssetClass.Etf => PriceSource.Stooq,
        AssetClass.Crypto => PriceSource.CoinGecko,
        AssetClass.PreciousMetal => PriceSource.Nbp,
        AssetClass.Cash or AssetClass.Deposit or AssetClass.Savings or AssetClass.Bond or AssetClass.RealEstate or AssetClass.Other => null,
        _ => throw new ArgumentOutOfRangeException(nameof(assetClass), assetClass, "Unmapped AssetClass — add it to AssetClassPriceSourceMapping.Resolve."),
    };
}
