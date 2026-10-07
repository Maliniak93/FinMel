using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;

namespace Skarbiec.Portfolio.Tests;

public sealed class AssetTransactionTypesTests
{
    private static readonly TransactionType[] CashLikeTypes = [TransactionType.Deposit, TransactionType.Withdraw];

    private static readonly TransactionType[] MetalTypes = [TransactionType.Buy, TransactionType.Sell];

    private static bool IsCashLike(AssetClass assetClass) =>
        assetClass is AssetClass.Cash or AssetClass.Deposit or AssetClass.Savings or AssetClass.Bond;

    private static bool Expected(AssetClass assetClass, TransactionType type) => assetClass switch
    {
        AssetClass.PreciousMetal => MetalTypes.Contains(type),
        _ => !IsCashLike(assetClass) || CashLikeTypes.Contains(type)
    };

    public static TheoryData<AssetClass, TransactionType, bool> FullMatrix()
    {
        var data = new TheoryData<AssetClass, TransactionType, bool>();
        foreach (var assetClass in Enum.GetValues<AssetClass>())
        {
            foreach (var type in Enum.GetValues<TransactionType>())
            {
                data.Add(assetClass, type, Expected(assetClass, type));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FullMatrix))]
    public void IsAllowed_MatchesClassRule(AssetClass assetClass, TransactionType type, bool expected)
    {
        Assert.Equal(expected, AssetTransactionTypes.IsAllowed(assetClass, type));
    }

    [Theory]
    [InlineData(AssetClass.Cash)]
    [InlineData(AssetClass.Deposit)]
    [InlineData(AssetClass.Savings)]
    [InlineData(AssetClass.Bond)]
    public void Allowed_CashLikeClass_ReturnsOnlyDepositAndWithdraw(AssetClass assetClass)
    {
        Assert.Equal(CashLikeTypes.Order(), AssetTransactionTypes.Allowed(assetClass).Order());
    }

    [Fact]
    public void Allowed_PreciousMetal_ReturnsOnlyBuyAndSell()
    {
        Assert.Equal(MetalTypes.Order(), AssetTransactionTypes.Allowed(AssetClass.PreciousMetal).Order());
    }

    [Theory]
    [InlineData(AssetClass.Stock)]
    [InlineData(AssetClass.Etf)]
    [InlineData(AssetClass.Crypto)]
    [InlineData(AssetClass.RealEstate)]
    [InlineData(AssetClass.Other)]
    public void Allowed_NonCashLikeClass_ReturnsEveryType(AssetClass assetClass)
    {
        Assert.Equal(Enum.GetValues<TransactionType>().Order(), AssetTransactionTypes.Allowed(assetClass).Order());
    }
}
