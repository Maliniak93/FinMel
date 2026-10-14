namespace Skarbiec.Contracts.Tests;

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
    public void Default_BankAccount_IsCurrencyValued()
    {
        Assert.Equal(AssetValuationMode.CurrencyValued, AssetValuationModes.Default(AssetClass.BankAccount));
    }

    [Fact]
    public void Default_Bond_IsCurrencyValued()
    {
        Assert.Equal(AssetValuationMode.CurrencyValued, AssetValuationModes.Default(AssetClass.Bond));
    }

    [Fact]
    public void EnumValues_ArePinned()
    {
        Assert.Equal(0, (int)AssetClass.Cash);
        Assert.Equal(1, (int)AssetClass.Deposit);
        Assert.Equal(2, (int)AssetClass.Stock);
        Assert.Equal(3, (int)AssetClass.Etf);
        Assert.Equal(4, (int)AssetClass.Bond);
        Assert.Equal(6, (int)AssetClass.PreciousMetal);
        Assert.Equal(9, (int)AssetClass.BankAccount);
        Assert.Equal(0, (int)AssetValuationMode.Market);
        Assert.Equal(2, (int)AssetValuationMode.CurrencyValued);
    }
}
