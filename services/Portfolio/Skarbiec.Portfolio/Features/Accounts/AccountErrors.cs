using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts;

internal static class AccountErrors
{
    public static Error NotFound(Guid id) =>
        new("NotFound.Account", $"Account '{id}' was not found.");

    public static Error DuplicateName(string name) =>
        new("Conflict.DuplicateName", $"An account named '{name}' already exists.");

    public static Error Archived(Guid id) =>
        new("Conflict.AccountArchived", $"Account '{id}' is archived — restore it to make changes.");

    public static Error NotEmpty(Guid id) =>
        new("Conflict.AccountNotEmpty", $"Account '{id}' still holds assets with a non-zero quantity.");

    public static Error HasHistory(Guid id) =>
        new("Conflict.AccountHasHistory", $"Account '{id}' holds assets, so it can't be deleted.");

    public static Error HasOpenDeposit(Guid id) =>
        new("Conflict.AccountHasOpenDeposit", $"Account '{id}' is the return account of a deposit that is not paid out yet.");

    public static Error HasRecurringIncome(Guid id) =>
        new("Conflict.AccountHasRecurringIncome", $"Account '{id}' is the target of a recurring income — move or delete the income first.");

    public static Error HasOneOffIncomes(Guid id) =>
        new("Conflict.AccountHasOneOffIncomes", $"Account '{id}' is the target of a one-off income — move or delete the income first, or undo its settlement.");

    public static Error HasIncomeOccurrences(Guid id) =>
        new("Conflict.AccountHasIncomeOccurrences", $"Account '{id}' is planned or settled by a recurring income occurrence — undo it first.");

    public static Error BankAccountRequired(Guid id) =>
        new("Validation.BankAccountRequired", $"Account '{id}' is not an unarchived Bank account.");

    public static Error CashManaged(Guid assetId) =>
        new("Conflict.AccountCashManaged", $"Asset '{assetId}' is an account's cash — change it through the account, its deposits, withdrawals and transfers.");

    public static Error Required(AssetClass assetClass, AccountType type) =>
        new("Validation.AccountRequired", $"A {assetClass} asset needs an accountId of a {type} account.");

    public static Error TypeMismatch(AssetClass assetClass, AccountType required, AccountType actual) =>
        new("Validation.AccountTypeMismatch", $"A {assetClass} asset needs a {required} account, not a {actual} one.");

    public static Error ContributionLimitNotAllowed { get; } =
        new("Validation.ContributionLimitNotAllowed", "Only an IKE or IKZE account has a contribution limit.");

    public static Error InvalidContributionLimit { get; } =
        new("Validation.ContributionLimit", "A contribution limit needs an amount above 0 and a year from 2000 to 2100.");

    public static Error ContributionLimitNotFound(Guid id, int year) =>
        new("NotFound.ContributionLimit", $"Account '{id}' has no contribution limit for {year}.");

    public static Error UseAccountEndpoints { get; } =
        new("Validation.UseAccountEndpoints", "A BankAccount asset is a bank account's money — create a Bank account instead.");

    public static Error BankVariantRequired { get; } =
        new("Validation.BankVariantRequired", "A Bank account needs a variant, Current or Savings.");

    public static Error WrapperNotAllowed { get; } =
        new("Validation.WrapperNotAllowed", "A Bank account has no tax wrapper — it must be Regular.");

    public static Error BankFieldsNotAllowed { get; } =
        new("Validation.BankFieldsNotAllowed", "Only a Bank account has a variant, bank name or rate, and only a Bank account can hold a currency other than PLN.");

    public static Error NotAllowed(AssetClass assetClass) =>
        new("Validation.AccountNotAllowed", $"A {assetClass} asset sits in no account.");
}
