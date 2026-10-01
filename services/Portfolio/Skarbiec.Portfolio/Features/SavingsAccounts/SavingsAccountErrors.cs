using System.Globalization;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.SavingsAccounts;

internal static class SavingsAccountErrors
{
    /// <summary>Also the answer for an asset that exists but is not a savings account — the savings-account endpoints address savings accounts only.</summary>
    public static Error NotFound(Guid assetId) =>
        new("NotFound.SavingsAccount", $"Savings account '{assetId}' was not found.");

    /// <summary>A Savings-class asset carries terms, so the generic asset endpoints never create, edit or re-class one.</summary>
    public static readonly Error UseSavingsAccountEndpoints =
        new("Validation.UseSavingsAccountEndpoints", "A Savings asset is a savings account — create and edit it through the savings-account endpoints.");

    /// <summary>The opening deposit is dated after today's Europe/Warsaw date.</summary>
    public static readonly Error OpeningDepositDateInFuture =
        new("Validation.OpeningDepositDateInFuture", "The opening deposit date can't be in the future.");

    /// <summary>Preview/settle while no ended, unsettled month of the account carries interest (savings-interest-settlement).</summary>
    public static readonly Error InterestNotDue =
        new("Conflict.SavingsInterestNotDue", "No interest period of this savings account is due — a month can be settled from the day after it ends.");

    /// <summary>The settle request's <c>periodEnd</c> is not the next due period's — a stale dialog or a double submit.</summary>
    public static Error InterestPeriodMismatch(DateOnly duePeriodEnd) =>
        new("Conflict.SavingsInterestPeriodMismatch", $"The next interest period to settle ends on {duePeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} — reload and try again.");

    public static Error SettlementNotFound(Guid settlementId) =>
        new("NotFound.SavingsInterestSettlement", $"Interest settlement '{settlementId}' was not found.");

    /// <summary>Only the latest settlement can be undone — periods settle, and unsettle, in order.</summary>
    public static readonly Error SettlementNotLatest =
        new("Conflict.SavingsSettlementNotLatest", "Only the latest interest settlement can be undone.");

    /// <summary>Update/DeleteTransaction on a settlement's interest credit — it goes only by undoing the settlement.</summary>
    public static readonly Error InterestManaged =
        new("Conflict.SavingsInterestManaged", "This transaction is a savings interest credit — undo the settlement instead.");
}
