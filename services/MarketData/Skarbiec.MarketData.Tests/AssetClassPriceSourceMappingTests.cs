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

    [Theory]
    [InlineData(AssetClass.Stock)]
    [InlineData(AssetClass.Etf)]
    [InlineData(AssetClass.PreciousMetal)]
    public void Resolve_MarketClasses_AreYahoo(AssetClass assetClass)
    {
        Assert.Equal(PriceSource.Yahoo, AssetClassPriceSourceMapping.Resolve(assetClass));
    }

    [Fact]
    public void Resolve_BankAccount_IsPricedByNoSource()
    {
        Assert.Null(AssetClassPriceSourceMapping.Resolve(AssetClass.BankAccount));
    }
}
