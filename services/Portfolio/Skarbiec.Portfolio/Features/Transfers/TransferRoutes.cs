using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Transfers;

public static class TransferRoutes
{
    [Flags]
    private enum Access
    {
        None = 0,
        Create = 1,
        Delete = 2
    }

    private static readonly Dictionary<(AssetClass Source, AssetClass Target), Access> Routes = new()
    {
        // Funding a term deposit from cash — entered through AddDeposit's fundingAssetId.
        [(AssetClass.Cash, AssetClass.Deposit)] = Access.None,

        // Buying a treasury bond with cash — entered through AddBond's fundingAssetId.
        [(AssetClass.Cash, AssetClass.Bond)] = Access.None,

        // Paying a settled deposit out to cash, through SettleDeposit's destinationAssetId and PayOutDeposit.
        [(AssetClass.Deposit, AssetClass.Cash)] = Access.None,

        // Paying a settled deposit out into a savings account, through the same two payout slices.
        [(AssetClass.Deposit, AssetClass.Savings)] = Access.None,

        // Paying a treasury bond's coupon out to cash, through SettleBondInterest's destinationAssetId.
        [(AssetClass.Bond, AssetClass.Cash)] = Access.None,

        // Buying a new series with a matured bond's proceeds, through SwapBond.
        [(AssetClass.Bond, AssetClass.Bond)] = Access.None,

        // Moving money between a current account and a savings account, through CreateTransfer and DeleteTransfer.
        [(AssetClass.Cash, AssetClass.Savings)] = Access.Create | Access.Delete,
        [(AssetClass.Savings, AssetClass.Cash)] = Access.Create | Access.Delete,

        // A metal Buy paid from cash and a Sell paid into it, through RecordTransaction's cashAssetId; removed through DeleteTransfer.
        [(AssetClass.Cash, AssetClass.PreciousMetal)] = Access.Delete,
        [(AssetClass.PreciousMetal, AssetClass.Cash)] = Access.Delete,

        // A stock or ETF Buy paid from cash and a Sell or Dividend paid into it, through RecordTransaction's cashAssetId; changed through the stock's own transaction.
        [(AssetClass.Cash, AssetClass.Stock)] = Access.None,
        [(AssetClass.Cash, AssetClass.Etf)] = Access.None,
        [(AssetClass.Stock, AssetClass.Cash)] = Access.None,
        [(AssetClass.Etf, AssetClass.Cash)] = Access.None,
    };

    public static bool IsAllowed(AssetClass source, AssetClass target) => Routes.ContainsKey((source, target));

    public static bool IsCreatable(AssetClass source, AssetClass target) => Has(source, target, Access.Create);

    public static bool IsDeletable(AssetClass source, AssetClass target) => Has(source, target, Access.Delete);

    private static bool Has(AssetClass source, AssetClass target, Access access) =>
        Routes.GetValueOrDefault((source, target)).HasFlag(access);
}
