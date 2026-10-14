using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferRoutesTests
{
    private static bool ExpectedAllowed(AssetClass source, AssetClass target) =>
        (source, target) is (AssetClass.Cash, AssetClass.Deposit) or (AssetClass.Deposit, AssetClass.Cash)
            or (AssetClass.Deposit, AssetClass.BankAccount)
            or (AssetClass.Cash, AssetClass.Bond) or (AssetClass.Bond, AssetClass.Cash)
            or (AssetClass.Bond, AssetClass.Bond)
            or (AssetClass.Cash, AssetClass.PreciousMetal) or (AssetClass.PreciousMetal, AssetClass.Cash)
            or (AssetClass.Cash, AssetClass.Stock) or (AssetClass.Stock, AssetClass.Cash)
            or (AssetClass.Cash, AssetClass.Etf) or (AssetClass.Etf, AssetClass.Cash);

    private static bool ExpectedDeletable(AssetClass source, AssetClass target) =>
        (source, target) is (AssetClass.Cash, AssetClass.PreciousMetal) or (AssetClass.PreciousMetal, AssetClass.Cash);

    public static TheoryData<AssetClass, AssetClass, bool, bool> EveryClassPair()
    {
        var data = new TheoryData<AssetClass, AssetClass, bool, bool>();
        foreach (var source in Enum.GetValues<AssetClass>())
        {
            foreach (var target in Enum.GetValues<AssetClass>())
            {
                data.Add(source, target, ExpectedAllowed(source, target), ExpectedDeletable(source, target));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryClassPair))]
    public void Route_EveryClassPair_OnlyTheListedRoutesAreAllowedAndOnlyCashMetalIsDeletable(
        AssetClass source, AssetClass target, bool allowed, bool deletable)
    {
        Assert.Equal(allowed, TransferRoutes.IsAllowed(source, target));
        Assert.Equal(deletable, TransferRoutes.IsDeletable(source, target));
    }

    [Theory]
    [InlineData(AssetClass.Cash, AssetClass.Cash)]
    [InlineData(AssetClass.Deposit, AssetClass.Deposit)]
    [InlineData(AssetClass.BankAccount, AssetClass.BankAccount)]
    [InlineData(AssetClass.Stock, AssetClass.Deposit)]
    [InlineData(AssetClass.BankAccount, AssetClass.Deposit)]
    [InlineData(AssetClass.Cash, AssetClass.BankAccount)]
    [InlineData(AssetClass.BankAccount, AssetClass.Cash)]
    public void IsAllowed_SameClassSecuritiesOrFreeCashBankPair_IsRejected(AssetClass source, AssetClass target)
    {
        Assert.False(TransferRoutes.IsAllowed(source, target));
        Assert.False(TransferRoutes.IsDeletable(source, target));
    }

    [Fact]
    public void Route_BondToBond_IsAllowedButNotDeletable()
    {
        Assert.True(TransferRoutes.IsAllowed(AssetClass.Bond, AssetClass.Bond));
        Assert.False(TransferRoutes.IsDeletable(AssetClass.Bond, AssetClass.Bond));
    }

    [Fact]
    public void Route_DepositToBankAccount_IsAllowedButNotDeletable()
    {
        Assert.True(TransferRoutes.IsAllowed(AssetClass.Deposit, AssetClass.BankAccount));
        Assert.False(TransferRoutes.IsDeletable(AssetClass.Deposit, AssetClass.BankAccount));
        Assert.False(TransferRoutes.IsAllowed(AssetClass.BankAccount, AssetClass.Deposit));
    }

    [Fact]
    public void Route_CashMetal_IsDeleteOnly()
    {
        Assert.True(TransferRoutes.IsDeletable(AssetClass.Cash, AssetClass.PreciousMetal));
        Assert.True(TransferRoutes.IsDeletable(AssetClass.PreciousMetal, AssetClass.Cash));
    }

    public static TheoryData<AssetClass, AssetClass> AccountMoneyPairs() => new()
    {
        { AssetClass.Cash, AssetClass.Cash },
        { AssetClass.Cash, AssetClass.BankAccount },
        { AssetClass.BankAccount, AssetClass.Cash },
        { AssetClass.BankAccount, AssetClass.BankAccount },
    };

    [Theory]
    [MemberData(nameof(AccountMoneyPairs))]
    public void IsCreatable_AccountMoneyOfTwoAccounts_IsCreatableAndDeletable(AssetClass sourceClass, AssetClass targetClass)
    {
        var source = Money(sourceClass, Guid.NewGuid());
        var target = Money(targetClass, Guid.NewGuid());

        Assert.True(TransferRoutes.IsCreatable(source, target));
        Assert.True(TransferRoutes.IsDeletable(source, target));
    }

    [Fact]
    public void IsCreatable_SameAccountOrFreeCash_IsRejected()
    {
        var accountId = Guid.NewGuid();
        var money = Money(AssetClass.BankAccount, accountId);
        var sameAccount = Money(AssetClass.Cash, accountId);
        var freeCash = Money(AssetClass.Cash, accountId: null);

        Assert.False(TransferRoutes.IsCreatable(money, sameAccount));
        Assert.False(TransferRoutes.IsCreatable(money, freeCash));
        Assert.False(TransferRoutes.IsCreatable(freeCash, money));
    }

    private static Asset Money(AssetClass assetClass, Guid? accountId) => new()
    {
        Id = Guid.NewGuid(),
        AccountId = accountId,
        AssetClass = assetClass,
        Name = "Money",
        Currency = "PLN",
        ValuationMode = AssetValuationMode.CurrencyValued
    };
}
