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

    public static Error Required(AssetClass assetClass, AccountType type) =>
        new("Validation.AccountRequired", $"A {assetClass} asset needs an accountId of a {type} account.");

    public static Error TypeMismatch(AssetClass assetClass, AccountType required, AccountType actual) =>
        new("Validation.AccountTypeMismatch", $"A {assetClass} asset needs a {required} account, not a {actual} one.");

    public static Error NotAllowed(AssetClass assetClass) =>
        new("Validation.AccountNotAllowed", $"A {assetClass} asset sits in no account.");
}
