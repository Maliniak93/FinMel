using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferRoutesTests
{
    private static bool ExpectedAllowed(AssetClass source, AssetClass target) =>
        (source, target) is (AssetClass.Cash, AssetClass.Deposit) or (AssetClass.Deposit, AssetClass.Cash)
            or (AssetClass.Cash, AssetClass.Savings) or (AssetClass.Savings, AssetClass.Cash)
            or (AssetClass.Deposit, AssetClass.Savings)
            or (AssetClass.Cash, AssetClass.Bond) or (AssetClass.Bond, AssetClass.Cash)
            or (AssetClass.Bond, AssetClass.Bond)
            or (AssetClass.Cash, AssetClass.PreciousMetal) or (AssetClass.PreciousMetal, AssetClass.Cash);

    private static bool ExpectedCreatable(AssetClass source, AssetClass target) =>
        (source, target) is (AssetClass.Cash, AssetClass.Savings) or (AssetClass.Savings, AssetClass.Cash);

    private static bool ExpectedDeletable(AssetClass source, AssetClass target) =>
        ExpectedCreatable(source, target)
        || (source, target) is (AssetClass.Cash, AssetClass.PreciousMetal) or (AssetClass.PreciousMetal, AssetClass.Cash);

    public static TheoryData<AssetClass, AssetClass, bool, bool, bool> EveryClassPair()
    {
        var data = new TheoryData<AssetClass, AssetClass, bool, bool, bool>();
        foreach (var source in Enum.GetValues<AssetClass>())
        {
            foreach (var target in Enum.GetValues<AssetClass>())
            {
                data.Add(source, target, ExpectedAllowed(source, target), ExpectedCreatable(source, target), ExpectedDeletable(source, target));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryClassPair))]
    public void Route_EveryClassPair_OnlyCashSavingsIsCreatableAndOnlyCashSavingsOrMetalIsDeletable(
        AssetClass source, AssetClass target, bool allowed, bool creatable, bool deletable)
    {
        Assert.Equal(allowed, TransferRoutes.IsAllowed(source, target));
        Assert.Equal(creatable, TransferRoutes.IsCreatable(source, target));
        Assert.Equal(deletable, TransferRoutes.IsDeletable(source, target));
    }

    [Theory]
    [InlineData(AssetClass.Cash, AssetClass.Cash)]
    [InlineData(AssetClass.Deposit, AssetClass.Deposit)]
    [InlineData(AssetClass.Savings, AssetClass.Savings)]
    [InlineData(AssetClass.Cash, AssetClass.Stock)]
    [InlineData(AssetClass.Stock, AssetClass.Deposit)]
    [InlineData(AssetClass.Savings, AssetClass.Deposit)]
    public void IsAllowed_SameClassSecuritiesOrSavingsDepositPair_IsRejected(AssetClass source, AssetClass target)
    {
        Assert.False(TransferRoutes.IsAllowed(source, target));
        Assert.False(TransferRoutes.IsCreatable(source, target));
        Assert.False(TransferRoutes.IsDeletable(source, target));
    }

    [Fact]
    public void Route_BondToBond_IsAllowedButNotManual()
    {
        Assert.True(TransferRoutes.IsAllowed(AssetClass.Bond, AssetClass.Bond));
        Assert.False(TransferRoutes.IsCreatable(AssetClass.Bond, AssetClass.Bond));
        Assert.False(TransferRoutes.IsDeletable(AssetClass.Bond, AssetClass.Bond));
    }

    [Fact]
    public void Route_DepositToSavings_IsAllowedButNotManual()
    {
        Assert.True(TransferRoutes.IsAllowed(AssetClass.Deposit, AssetClass.Savings));
        Assert.False(TransferRoutes.IsCreatable(AssetClass.Deposit, AssetClass.Savings));
        Assert.False(TransferRoutes.IsDeletable(AssetClass.Deposit, AssetClass.Savings));
        Assert.False(TransferRoutes.IsAllowed(AssetClass.Savings, AssetClass.Deposit));
    }

    [Fact]
    public void Route_CashMetal_IsDeleteOnly()
    {
        Assert.True(TransferRoutes.IsDeletable(AssetClass.Cash, AssetClass.PreciousMetal));
        Assert.True(TransferRoutes.IsDeletable(AssetClass.PreciousMetal, AssetClass.Cash));
        Assert.False(TransferRoutes.IsCreatable(AssetClass.Cash, AssetClass.PreciousMetal));
        Assert.False(TransferRoutes.IsCreatable(AssetClass.PreciousMetal, AssetClass.Cash));
    }
}
