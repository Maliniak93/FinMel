using System.Globalization;
using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Accounts.BankInterest;

internal static class BankInterestErrors
{
    public static readonly Error NotSupported =
        new("Validation.InterestNotSupported", "Only a Bank account earns interest.");

    public static readonly Error NotDue =
        new("Conflict.BankInterestNotDue", "No interest period of this bank account is due — a month can be settled from the day after it ends.");

    public static Error PeriodMismatch(DateOnly duePeriodEnd) =>
        new("Conflict.BankInterestPeriodMismatch", $"The next interest period to settle ends on {duePeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} — reload and try again.");

    public static Error SettlementNotFound(Guid settlementId) =>
        new("NotFound.BankInterestSettlement", $"Interest settlement '{settlementId}' was not found.");

    public static readonly Error SettlementNotLatest =
        new("Conflict.BankSettlementNotLatest", "Only the latest interest settlement can be undone.");

    public static readonly Error InterestManaged =
        new("Conflict.BankInterestManaged", "This transaction is a bank interest credit — undo the settlement instead.");
}
