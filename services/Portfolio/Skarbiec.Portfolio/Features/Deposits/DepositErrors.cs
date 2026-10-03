using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Deposits;

internal static class DepositErrors
{
    public static Error NotFound(Guid assetId) =>
        new("NotFound.Deposit", $"Deposit '{assetId}' was not found.");

    public static readonly Error UseDepositEndpoints =
        new("Validation.UseDepositEndpoints", "A Deposit asset is a term deposit — create and edit it through the deposit endpoints.");

    public static readonly Error TransactionsManaged =
        new("Conflict.DepositTransactionsManaged", "A term deposit's transactions are managed by the deposit itself — edit the deposit instead.");

    public static readonly Error NotDue =
        new("Conflict.DepositNotDue", "This deposit has not matured yet — it can be settled from its maturity date on.");

    public static readonly Error AlreadySettled =
        new("Conflict.DepositAlreadySettled", "This deposit is already settled.");

    public static readonly Error Settled =
        new("Conflict.DepositSettled", "A settled deposit's terms can't be changed — delete and re-create it instead.");

    public static readonly Error SettledOnBeforeStart =
        new("Validation.SettledOnBeforeStart", "The settlement date can't be before the deposit's start date.");

    public static readonly Error SettledOnInFuture =
        new("Validation.SettledOnInFuture", "The settlement date can't be in the future.");

    public static readonly Error NotSettled =
        new("Conflict.DepositNotSettled", "This deposit is not settled yet — settle it before paying it out.");

    public static readonly Error AlreadyPaidOut =
        new("Conflict.DepositAlreadyPaidOut", "This deposit is already paid out.");

    public static readonly Error PayoutDateBeforeSettlement =
        new("Validation.PayoutDateBeforeSettlement", "The payout date can't be before the deposit's settlement date.");

    public static readonly Error PayoutDateInFuture =
        new("Validation.PayoutDateInFuture", "The payout date can't be in the future.");

    public static readonly Error SettlementAmountsRequired =
        new("Validation.SettlementAmountsRequired", "The gross interest and the tax are required to roll over a deposit that is not settled yet.");

    public static readonly Error RolledOver =
        new("Conflict.DepositRolledOver", "A rolled-over deposit's principal and start date can't be changed.");
}
