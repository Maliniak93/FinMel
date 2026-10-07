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
    public void Resolve_StockAndEtf_AreYahoo(AssetClass assetClass)
    {
        Assert.Equal(PriceSource.Yahoo, AssetClassPriceSourceMapping.Resolve(assetClass));
    }

    [Fact]
    public void MarketDataAssembly_HasNoTypeNamedStooq()
    {
        var stooqTypes = typeof(PriceSource).Assembly.GetTypes().Where(t => t.Name.StartsWith("Stooq", StringComparison.Ordinal));

        Assert.Empty(stooqTypes);
    }

    [Fact]
    public void Resolve_PreciousMetal_IsPricedByGoldApi()
    {
        Assert.Equal(PriceSource.GoldApi, AssetClassPriceSourceMapping.Resolve(AssetClass.PreciousMetal));
    }

    [Fact]
    public void Resolve_Savings_IsPricedByNoSource()
    {
        Assert.Null(AssetClassPriceSourceMapping.Resolve(AssetClass.Savings));
    }
}
