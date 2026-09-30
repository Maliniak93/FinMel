using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Tests;

/// <summary>savings-accounts AC-1: every <see cref="AssetClass"/> resolves to a price source (or none) without throwing.</summary>
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
