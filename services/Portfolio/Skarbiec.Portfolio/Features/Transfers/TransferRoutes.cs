using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts;
using Skarbiec.Portfolio.Features.CashPlaces;

namespace Skarbiec.Portfolio.Features.Transfers;

public static class TransferRoutes
{
    [Flags]
    private enum Access
    {
        None = 0,
        Delete = 1
    }

    private static readonly Dictionary<(AssetClass Source, AssetClass Target), Access> Routes = new()
    {
        // Opening a term deposit from a bank account, through AddDeposit and UpdateDeposit.
        [(AssetClass.BankAccount, AssetClass.Deposit)] = Access.None,

        // A treasury bond's purchase and a swap's shortfall, paid from its own account's cash by AddBond, UpdateBond and SwapBond.
        [(AssetClass.Cash, AssetClass.Bond)] = Access.None,

        // Paying a settled deposit out into its return account, through SettleDeposit's payOut and PayOutDeposit.
        [(AssetClass.Deposit, AssetClass.BankAccount)] = Access.None,

        // A treasury bond's coupon, redemption proceeds and swap leftover, paid to its own account's cash by SettleBondInterest, RedeemBond, RedeemBondEarly and SwapBond.
        [(AssetClass.Bond, AssetClass.Cash)] = Access.None,

        // Buying a new series with a matured bond's proceeds, through SwapBond.
        [(AssetClass.Bond, AssetClass.Bond)] = Access.None,

        // A metal Buy paid from cash and a Sell paid into it, through RecordTransaction's cashAssetId; removed through DeleteTransfer.
        [(AssetClass.Cash, AssetClass.PreciousMetal)] = Access.Delete,
        [(AssetClass.PreciousMetal, AssetClass.Cash)] = Access.Delete,

        // A stock or ETF trade, dividend or interest settles only in its own account's cash, written by RecordTransaction and AddAsset; changed through the security's own transaction.
        [(AssetClass.Cash, AssetClass.Stock)] = Access.None,
        [(AssetClass.Cash, AssetClass.Etf)] = Access.None,
        [(AssetClass.Stock, AssetClass.Cash)] = Access.None,
        [(AssetClass.Etf, AssetClass.Cash)] = Access.None,
    };

    // An account's money moves to another account's money, and a bank account's money to or from a cash place balance (an ATM withdrawal, a bank deposit).
    public static bool IsCreatable(Asset source, Asset target) =>
        (source.IsAccountCash() && target.IsAccountCash() && source.AccountId != target.AccountId)
        || IsBankAndPlace(source, target);

    public static bool IsDeletable(Asset source, Asset target) =>
        (source.IsAccountCash() && target.IsAccountCash()) || IsBankAndPlace(source, target) || IsDeletable(source.AssetClass, target.AssetClass);

    private static bool IsBankAndPlace(Asset source, Asset target) =>
        (IsBankMoney(source) && target.IsCashPlaceBalance()) || (source.IsCashPlaceBalance() && IsBankMoney(target));

    private static bool IsBankMoney(Asset asset) => asset.IsAccountCash() && asset.AssetClass == AssetClass.BankAccount;

    public static bool IsAllowed(AssetClass source, AssetClass target) => Routes.ContainsKey((source, target));

    public static bool IsDeletable(AssetClass source, AssetClass target) => Has(source, target, Access.Delete);

    private static bool Has(AssetClass source, AssetClass target, Access access) =>
        Routes.GetValueOrDefault((source, target)).HasFlag(access);
}
