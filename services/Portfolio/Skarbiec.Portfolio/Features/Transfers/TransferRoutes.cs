using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Transfers;

/// <summary>
/// The route policy of the generic transfer core (asset-transfers-deposit-funding): which source
/// class may move money into which target class. A route either enters through its own slice, or is
/// <b>manual</b> (savings-cash-transfers) — created and deleted only through the generic
/// <c>/transfers</c> endpoints, which accept nothing else. A new route is one entry here (plus its
/// slice when it is not manual). Every pair not listed is rejected.
/// </summary>
public static class TransferRoutes
{
    private static readonly Dictionary<(AssetClass Source, AssetClass Target), bool> Routes = new()
    {
        // Funding a term deposit from cash — entered through AddDeposit's fundingAssetId.
        [(AssetClass.Cash, AssetClass.Deposit)] = false,

        // Paying a settled deposit out to cash — entered through SettleDeposit's destinationAssetId and
        // PayOutDeposit (deposit-payout-to-cash).
        [(AssetClass.Deposit, AssetClass.Cash)] = false,

        // Paying a settled deposit out into a savings account in its currency — the same two payout
        // slices (deposit-payout-to-savings).
        [(AssetClass.Deposit, AssetClass.Savings)] = false,

        // Moving money between a current account and a savings account, either way — CreateTransfer /
        // DeleteTransfer (savings-cash-transfers).
        [(AssetClass.Cash, AssetClass.Savings)] = true,
        [(AssetClass.Savings, AssetClass.Cash)] = true,
    };

    public static bool IsAllowed(AssetClass source, AssetClass target) => Routes.ContainsKey((source, target));

    /// <summary>An allowed route the generic <c>/transfers</c> endpoints create and delete.</summary>
    public static bool IsManual(AssetClass source, AssetClass target) => Routes.GetValueOrDefault((source, target));
}
