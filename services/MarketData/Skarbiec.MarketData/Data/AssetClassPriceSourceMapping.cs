using Skarbiec.Contracts;

namespace Skarbiec.MarketData.Data;

public static class AssetClassPriceSourceMapping
{
    public static PriceSource? Resolve(AssetClass assetClass) => assetClass switch
    {
        AssetClass.Stock or AssetClass.Etf or AssetClass.PreciousMetal => PriceSource.Yahoo,
        AssetClass.Cash or AssetClass.Deposit or AssetClass.BankAccount or AssetClass.Bond => null,
        _ => throw new ArgumentOutOfRangeException(nameof(assetClass), assetClass, "Unmapped AssetClass — add it to AssetClassPriceSourceMapping.Resolve."),
    };
}
