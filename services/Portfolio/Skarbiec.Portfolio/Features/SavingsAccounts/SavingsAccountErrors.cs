using System.Globalization;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.SavingsAccounts;

internal static class SavingsAccountErrors
{
    public static Error NotFound(Guid assetId) =>
        new("NotFound.SavingsAccount", $"Savings account '{assetId}' was not found.");

    public static readonly Error UseSavingsAccountEndpoints =
        new("Validation.UseSavingsAccountEndpoints", "A Savings asset is a savings account — create and edit it through the savings-account endpoints.");

    public static readonly Error OpeningDepositDateInFuture =
        new("Validation.OpeningDepositDateInFuture", "The opening deposit date can't be in the future.");

    public static readonly Error InterestNotDue =
        new("Conflict.SavingsInterestNotDue", "No interest period of this savings account is due — a month can be settled from the day after it ends.");

    public static Error InterestPeriodMismatch(DateOnly duePeriodEnd) =>
        new("Conflict.SavingsInterestPeriodMismatch", $"The next interest period to settle ends on {duePeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} — reload and try again.");

    public static Error SettlementNotFound(Guid settlementId) =>
        new("NotFound.SavingsInterestSettlement", $"Interest settlement '{settlementId}' was not found.");

    public static readonly Error SettlementNotLatest =
        new("Conflict.SavingsSettlementNotLatest", "Only the latest interest settlement can be undone.");

    public static readonly Error InterestManaged =
        new("Conflict.SavingsInterestManaged", "This transaction is a savings interest credit — undo the settlement instead.");
}
