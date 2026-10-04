using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Transfers;

public static class TransferRoutes
{
    private static readonly Dictionary<(AssetClass Source, AssetClass Target), bool> Routes = new()
    {
        // Funding a term deposit from cash — entered through AddDeposit's fundingAssetId.
        [(AssetClass.Cash, AssetClass.Deposit)] = false,

        // Buying a treasury bond with cash — entered through AddBond's fundingAssetId.
        [(AssetClass.Cash, AssetClass.Bond)] = false,

        // Paying a settled deposit out to cash, through SettleDeposit's destinationAssetId and PayOutDeposit.
        [(AssetClass.Deposit, AssetClass.Cash)] = false,

        // Paying a settled deposit out into a savings account, through the same two payout slices.
        [(AssetClass.Deposit, AssetClass.Savings)] = false,

        // Paying a treasury bond's coupon out to cash, through SettleBondInterest's destinationAssetId.
        [(AssetClass.Bond, AssetClass.Cash)] = false,

        // Moving money between a current account and a savings account, through CreateTransfer and DeleteTransfer.
        [(AssetClass.Cash, AssetClass.Savings)] = true,
        [(AssetClass.Savings, AssetClass.Cash)] = true,
    };

    public static bool IsAllowed(AssetClass source, AssetClass target) => Routes.ContainsKey((source, target));

    public static bool IsManual(AssetClass source, AssetClass target) => Routes.GetValueOrDefault((source, target));
}
