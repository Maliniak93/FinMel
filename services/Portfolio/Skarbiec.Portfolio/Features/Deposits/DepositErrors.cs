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

    /// <summary>A term deposit's transactions — the opening one, rewritten by UpdateDeposit, and SettleDeposit's net-interest credit — are never written by hand.</summary>
    public static readonly Error TransactionsManaged =
        new("Conflict.DepositTransactionsManaged", "A term deposit's transactions are managed by the deposit itself — edit the deposit instead.");

    /// <summary>Preview/settle before the maturity date (Europe/Warsaw) — the deposit is not Due yet.</summary>
    public static readonly Error NotDue =
        new("Conflict.DepositNotDue", "This deposit has not matured yet — it can be settled from its maturity date on.");

    /// <summary>Preview/settle of a deposit that is already settled — undoing a settlement is not supported.</summary>
    public static readonly Error AlreadySettled =
        new("Conflict.DepositAlreadySettled", "This deposit is already settled.");

    /// <summary>UpdateDeposit on a settled deposit — its terms are immutable once settled.</summary>
    public static readonly Error Settled =
        new("Conflict.DepositSettled", "A settled deposit's terms can't be changed — delete and re-create it instead.");

    public static readonly Error SettledOnBeforeStart =
        new("Validation.SettledOnBeforeStart", "The settlement date can't be before the deposit's start date.");

    public static readonly Error SettledOnInFuture =
        new("Validation.SettledOnInFuture", "The settlement date can't be in the future.");
}
