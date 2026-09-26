using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Transfers;

/// <summary>
/// The route policy of the generic transfer core (asset-transfers-deposit-funding): which source
/// class may move money into which target class. Each route enters through its own slice — a new
/// route is one entry here plus that slice. Every pair not listed is rejected.
/// </summary>
public static class TransferRoutes
{
    private static readonly HashSet<(AssetClass Source, AssetClass Target)> Allowed =
    [
        // Funding a term deposit from cash — entered through AddDeposit's fundingAssetId.
        (AssetClass.Cash, AssetClass.Deposit),

        // Paying a matured deposit out to cash — registered for term-deposits part 4, no entry point yet.
        (AssetClass.Deposit, AssetClass.Cash),
    ];

    public static bool IsAllowed(AssetClass source, AssetClass target) => Allowed.Contains((source, target));
}
