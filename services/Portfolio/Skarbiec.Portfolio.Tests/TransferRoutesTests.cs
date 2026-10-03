using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// The route policy of the generic transfer core, pure and hostless. Every ordered pair of asset
/// classes is checked: Cash → Deposit, Deposit → Cash, Cash → Savings and Savings → Cash are allowed
/// (asset-transfers-deposit-funding AC-2, savings-cash-transfers AC-1), and only the last two are
/// manual — the only routes the generic CreateTransfer / DeleteTransfer endpoints accept.
/// </summary>
public sealed class TransferRoutesTests
{
    private static bool ExpectedAllowed(AssetClass source, AssetClass target) =>
        (source, target) is (AssetClass.Cash, AssetClass.Deposit) or (AssetClass.Deposit, AssetClass.Cash)
            or (AssetClass.Cash, AssetClass.Savings) or (AssetClass.Savings, AssetClass.Cash)
            or (AssetClass.Deposit, AssetClass.Savings);

    private static bool ExpectedManual(AssetClass source, AssetClass target) =>
        (source, target) is (AssetClass.Cash, AssetClass.Savings) or (AssetClass.Savings, AssetClass.Cash);

    public static TheoryData<AssetClass, AssetClass, bool, bool> EveryClassPair()
    {
        var data = new TheoryData<AssetClass, AssetClass, bool, bool>();
        foreach (var source in Enum.GetValues<AssetClass>())
        {
            foreach (var target in Enum.GetValues<AssetClass>())
            {
                data.Add(source, target, ExpectedAllowed(source, target), ExpectedManual(source, target));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryClassPair))]
    public void Route_EveryClassPair_OnlyTheListedRoutesAreAllowedAndOnlyCashSavingsIsManual(
        AssetClass source, AssetClass target, bool allowed, bool manual)
    {
        Assert.Equal(allowed, TransferRoutes.IsAllowed(source, target));
        Assert.Equal(manual, TransferRoutes.IsManual(source, target));
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
        Assert.False(TransferRoutes.IsManual(source, target));
    }

    /// <summary>AC-1: Deposit → Savings is allowed and deposit-owned (not manual); the reverse is not a route.</summary>
    [Fact]
    public void Route_DepositToSavings_IsAllowedButNotManual()
    {
        Assert.True(TransferRoutes.IsAllowed(AssetClass.Deposit, AssetClass.Savings));
        Assert.False(TransferRoutes.IsManual(AssetClass.Deposit, AssetClass.Savings));
        Assert.False(TransferRoutes.IsAllowed(AssetClass.Savings, AssetClass.Deposit));
    }
}
