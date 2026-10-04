using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Bonds;

internal static class BondErrors
{
    public static Error NotFound(Guid assetId) =>
        new("NotFound.Bond", $"Treasury bond '{assetId}' was not found.");

    public static readonly Error UseBondEndpoints =
        new("Validation.UseBondEndpoints", "A Bond asset is a treasury bond — create and edit it through the bond endpoints.");

    public static readonly Error TransactionsManaged =
        new("Conflict.BondTransactionsManaged", "A treasury bond's transactions are managed by the bond itself — edit the bond instead.");

    public static readonly Error Settled =
        new("Conflict.BondSettled", "This bond has settled interest periods — undo them before editing its terms.");

    public static Error InterestPeriodMismatch(int nextPeriodIndex) =>
        new("Conflict.BondInterestPeriodMismatch", $"The next interest period to settle is period {nextPeriodIndex}; send the next unsettled periods in order — reload and try again.");

    public static readonly Error InterestNotDue =
        new("Conflict.BondInterestNotDue", "An interest period can be settled only once it has ended.");

    public static readonly Error PeriodRate =
        new("Validation.BondPeriodRate", "A fixed-rate or first period takes the rate from the bond's terms and must not send one; every other period needs a rate from 0 to 100 with at most 4 decimal places.");

    public static readonly Error PayoutDestinationRequired =
        new("Validation.BondPayoutDestinationRequired", "A coupon bond pays its interest out — pick the Cash asset that receives it.");

    public static readonly Error PayoutDestinationNotAllowed =
        new("Validation.BondPayoutDestinationNotAllowed", "A capitalising bond adds its interest to its own value — it takes no payout destination.");

    public static Error SettlementNotFound(Guid settlementId) =>
        new("NotFound.BondInterestSettlement", $"Interest settlement '{settlementId}' was not found.");

    public static readonly Error SettlementNotLatest =
        new("Conflict.BondSettlementNotLatest", "Only the latest interest settlement can be undone.");

    public static readonly Error SettlementTransferDetached =
        new("Conflict.BondSettlementTransferDetached", "The Cash side of this coupon payout no longer exists, so the settlement can't be undone.");
}
