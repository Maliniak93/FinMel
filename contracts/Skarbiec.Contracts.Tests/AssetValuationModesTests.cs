namespace Skarbiec.Contracts.Tests;

/// <summary>savings-accounts AC-1: every <see cref="AssetClass"/> maps to a valuation mode, and Savings is currency-valued.</summary>
public sealed class AssetValuationModesTests
{
    [Fact]
    public void Default_MapsEveryAssetClass()
    {
        foreach (var assetClass in Enum.GetValues<AssetClass>())
        {
            var exception = Record.Exception(() => AssetValuationModes.Default(assetClass));

            Assert.Null(exception);
        }
    }

    [Fact]
    public void Default_Savings_IsCurrencyValued()
    {
        Assert.Equal(AssetValuationMode.CurrencyValued, AssetValuationModes.Default(AssetClass.Savings));
    }

    [Fact]
    public void Savings_IsAppendedWithStableValue()
    {
        Assert.Equal(9, (int)AssetClass.Savings);
        Assert.Equal(8, (int)AssetClass.Other);
    }
}
