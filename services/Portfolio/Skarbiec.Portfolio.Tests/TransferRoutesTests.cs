using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Transfers;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferRoutesTests
{
    private static bool ExpectedAllowed(AssetClass source, AssetClass target) =>
        (source, target) is (AssetClass.BankAccount, AssetClass.Deposit) or (AssetClass.Deposit, AssetClass.BankAccount)
            or (AssetClass.Cash, AssetClass.Bond) or (AssetClass.Bond, AssetClass.Cash)
            or (AssetClass.Bond, AssetClass.Bond)
            or (AssetClass.Cash, AssetClass.PreciousMetal) or (AssetClass.PreciousMetal, AssetClass.Cash)
            or (AssetClass.BankAccount, AssetClass.PreciousMetal) or (AssetClass.PreciousMetal, AssetClass.BankAccount)
            or (AssetClass.Cash, AssetClass.Stock) or (AssetClass.Stock, AssetClass.Cash)
            or (AssetClass.Cash, AssetClass.Etf) or (AssetClass.Etf, AssetClass.Cash);

    private static bool ExpectedDeletable(AssetClass source, AssetClass target) =>
        (source, target) is (AssetClass.Cash, AssetClass.PreciousMetal) or (AssetClass.PreciousMetal, AssetClass.Cash)
            or (AssetClass.BankAccount, AssetClass.PreciousMetal) or (AssetClass.PreciousMetal, AssetClass.BankAccount);

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
    public void Route_EveryClassPair_OnlyTheListedRoutesAreAllowedAndOnlyMetalSettlementIsDeletable(
        AssetClass source, AssetClass target, bool allowed, bool deletable)
    {
        Assert.Equal(allowed, TransferRoutes.IsAllowed(source, target));
        Assert.Equal(deletable, TransferRoutes.IsDeletable(source, target));
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

    [Fact]
    public void IsCreatable_BankMoneyAndCashPlaceBalance_IsCreatableAndDeletableBothWays()
    {
        var bank = Money(AssetClass.BankAccount, Guid.NewGuid());
        var placeBalance = PlaceBalance();

        Assert.True(TransferRoutes.IsCreatable(bank, placeBalance));
        Assert.True(TransferRoutes.IsCreatable(placeBalance, bank));
        Assert.True(TransferRoutes.IsDeletable(bank, placeBalance));
        Assert.True(TransferRoutes.IsDeletable(placeBalance, bank));
    }

    [Fact]
    public void IsCreatable_CashPlaceBalanceToPlaceOrToBrokerageCash_IsRejected()
    {
        var placeBalance = PlaceBalance();
        var otherPlaceBalance = PlaceBalance();
        var brokerageCash = Money(AssetClass.Cash, Guid.NewGuid());

        Assert.False(TransferRoutes.IsCreatable(placeBalance, otherPlaceBalance));
        Assert.False(TransferRoutes.IsCreatable(placeBalance, brokerageCash));
        Assert.False(TransferRoutes.IsCreatable(brokerageCash, placeBalance));
    }

    private static Asset PlaceBalance() => new()
    {
        Id = Guid.NewGuid(),
        CashPlaceId = Guid.NewGuid(),
        AssetClass = AssetClass.Cash,
        Name = "Sejf",
        Currency = "PLN",
        ValuationMode = AssetValuationMode.CurrencyValued
    };

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
