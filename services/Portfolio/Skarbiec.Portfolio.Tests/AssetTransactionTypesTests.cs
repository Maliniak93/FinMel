using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// cash-transaction-types AC-1: the single source of the "which transaction types does a class
/// accept" rule. Pure — no containers, no host. The expected matrix is written out independently of
/// the production rule: Cash and Deposit accept exactly Deposit/Withdraw, every other class all six.
/// </summary>
public sealed class AssetTransactionTypesTests
{
    private static readonly TransactionType[] CashLikeTypes = [TransactionType.Deposit, TransactionType.Withdraw];

    private static bool IsCashLike(AssetClass assetClass) =>
        assetClass is AssetClass.Cash or AssetClass.Deposit;

    public static TheoryData<AssetClass, TransactionType, bool> FullMatrix()
    {
        var data = new TheoryData<AssetClass, TransactionType, bool>();
        foreach (var assetClass in Enum.GetValues<AssetClass>())
        {
            foreach (var type in Enum.GetValues<TransactionType>())
            {
                data.Add(assetClass, type, !IsCashLike(assetClass) || CashLikeTypes.Contains(type));
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
    public void Allowed_CashLikeClass_ReturnsOnlyDepositAndWithdraw(AssetClass assetClass)
    {
        Assert.Equal(CashLikeTypes.Order(), AssetTransactionTypes.Allowed(assetClass).Order());
    }

    [Theory]
    [InlineData(AssetClass.Stock)]
    [InlineData(AssetClass.Etf)]
    [InlineData(AssetClass.Bond)]
    [InlineData(AssetClass.Crypto)]
    [InlineData(AssetClass.PreciousMetal)]
    [InlineData(AssetClass.RealEstate)]
    [InlineData(AssetClass.Other)]
    public void Allowed_NonCashLikeClass_ReturnsEveryType(AssetClass assetClass)
    {
        Assert.Equal(Enum.GetValues<TransactionType>().Order(), AssetTransactionTypes.Allowed(assetClass).Order());
    }
}
