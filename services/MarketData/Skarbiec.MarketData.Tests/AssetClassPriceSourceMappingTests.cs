using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Tests;

public sealed class AssetClassPriceSourceMappingTests
{
    [Fact]
    public void Resolve_MapsEveryAssetClass()
    {
        foreach (var assetClass in Enum.GetValues<AssetClass>())
        {
            var exception = Record.Exception(() => AssetClassPriceSourceMapping.Resolve(assetClass));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void Resolve_Savings_IsPricedByNoSource()
    {
        Assert.Null(AssetClassPriceSourceMapping.Resolve(AssetClass.Savings));
    }
}
