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

    /// <summary>A term deposit's only transaction is the opening one, rewritten by UpdateDeposit — never by hand.</summary>
    public static readonly Error TransactionsManaged =
        new("Conflict.DepositTransactionsManaged", "A term deposit's transactions are managed by the deposit itself — edit the deposit instead.");
}
