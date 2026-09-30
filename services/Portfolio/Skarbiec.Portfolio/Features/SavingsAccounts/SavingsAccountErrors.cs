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
}
