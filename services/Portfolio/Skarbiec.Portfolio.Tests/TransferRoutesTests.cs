using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// asset-transfers-deposit-funding AC-2: the route policy of the generic transfer core. Pure, no
/// host — every ordered pair of asset classes is checked, and only Cash → Deposit (funding a
/// deposit) and Deposit → Cash (registered for the payout, part 4) are allowed.
/// </summary>
public sealed class TransferRoutesTests
{
    public static TheoryData<AssetClass, AssetClass, bool> EveryClassPair()
    {
        var data = new TheoryData<AssetClass, AssetClass, bool>();
        foreach (var source in Enum.GetValues<AssetClass>())
        {
            foreach (var target in Enum.GetValues<AssetClass>())
            {
                var allowed = (source, target) is (AssetClass.Cash, AssetClass.Deposit) or (AssetClass.Deposit, AssetClass.Cash);
                data.Add(source, target, allowed);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryClassPair))]
    public void IsAllowed_EveryClassPair_OnlyCashToDepositAndDepositToCash(AssetClass source, AssetClass target, bool expected)
    {
        Assert.Equal(expected, TransferRoutes.IsAllowed(source, target));
    }

    [Theory]
    [InlineData(AssetClass.Cash, AssetClass.Cash)]
    [InlineData(AssetClass.Deposit, AssetClass.Deposit)]
    [InlineData(AssetClass.Cash, AssetClass.Stock)]
    [InlineData(AssetClass.Stock, AssetClass.Deposit)]
    public void IsAllowed_SameClassOrSecurities_IsRejected(AssetClass source, AssetClass target)
    {
        Assert.False(TransferRoutes.IsAllowed(source, target));
    }
}
