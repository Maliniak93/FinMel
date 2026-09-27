using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Deposits;

internal static class DepositErrors
{
    /// <summary>Also the answer for an asset that exists but is not a term deposit — the deposit endpoints address deposits only.</summary>
    public static Error NotFound(Guid assetId) =>
        new("NotFound.Deposit", $"Deposit '{assetId}' was not found.");

    /// <summary>A Deposit-class asset carries terms, so the generic asset endpoints never create, edit or re-class one.</summary>
    public static readonly Error UseDepositEndpoints =
        new("Validation.UseDepositEndpoints", "A Deposit asset is a term deposit — create and edit it through the deposit endpoints.");

    /// <summary>A term deposit's transactions — the opening one, rewritten by UpdateDeposit, SettleDeposit's net-interest credit and the payout's Withdraw leg — are never written by hand.</summary>
    public static readonly Error TransactionsManaged =
        new("Conflict.DepositTransactionsManaged", "A term deposit's transactions are managed by the deposit itself — edit the deposit instead.");

    /// <summary>Preview/settle before the maturity date (Europe/Warsaw) — the deposit is not Due yet.</summary>
    public static readonly Error NotDue =
        new("Conflict.DepositNotDue", "This deposit has not matured yet — it can be settled from its maturity date on.");

    /// <summary>
    /// Preview/settle of a deposit that is already settled — undoing a settlement is not supported — and a
    /// rollover of a settled deposit sent settlement amounts (deposit-rollover).
    /// </summary>
    public static readonly Error AlreadySettled =
        new("Conflict.DepositAlreadySettled", "This deposit is already settled.");

    /// <summary>UpdateDeposit on a settled deposit — its terms are immutable once settled.</summary>
    public static readonly Error Settled =
        new("Conflict.DepositSettled", "A settled deposit's terms can't be changed — delete and re-create it instead.");

    public static readonly Error SettledOnBeforeStart =
        new("Validation.SettledOnBeforeStart", "The settlement date can't be before the deposit's start date.");

    public static readonly Error SettledOnInFuture =
        new("Validation.SettledOnInFuture", "The settlement date can't be in the future.");

    /// <summary>PayOutDeposit on an Active or Due deposit — an early break is not supported (deposit-payout-to-cash).</summary>
    public static readonly Error NotSettled =
        new("Conflict.DepositNotSettled", "This deposit is not settled yet — settle it before paying it out.");

    /// <summary>PayOutDeposit on a deposit that already holds 0 — a payout always moves the whole balance.</summary>
    public static readonly Error AlreadyPaidOut =
        new("Conflict.DepositAlreadyPaidOut", "This deposit is already paid out.");

    public static readonly Error PayoutDateBeforeSettlement =
        new("Validation.PayoutDateBeforeSettlement", "The payout date can't be before the deposit's settlement date.");

    public static readonly Error PayoutDateInFuture =
        new("Validation.PayoutDateInFuture", "The payout date can't be in the future.");

    /// <summary>RollOverDeposit on a Due deposit without <c>grossInterest</c> or <c>tax</c> — it settles and rolls over in one save (deposit-rollover).</summary>
    public static readonly Error SettlementAmountsRequired =
        new("Validation.SettlementAmountsRequired", "The gross interest and the tax are required to roll over a deposit that is not settled yet.");

    /// <summary>UpdateDeposit changing a rolled-over deposit's principal or start date — earlier terms' balance produced them (deposit-rollover).</summary>
    public static readonly Error RolledOver =
        new("Conflict.DepositRolledOver", "A rolled-over deposit's principal and start date can't be changed.");
}
