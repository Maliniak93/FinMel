using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Accounts.BankInterest;

namespace Skarbiec.Portfolio.Features.Accounts;

public sealed record AccountResponse
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required AccountType Type { get; init; }
    public required TaxWrapper Wrapper { get; init; }
    public required string Currency { get; init; }
    public required bool IsArchived { get; init; }
    public required Guid CashAssetId { get; init; }

    /// <summary>The default group of new assets; for a Bank account, the group of its balance.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>The account's money in its currency, the quantity of its cash asset.</summary>
    public required decimal CashBalance { get; init; }

    /// <summary>Set only by the account reads, for an IKE or IKZE account; null for a Regular one.</summary>
    public ContributionResponse? Contribution { get; init; }

    /// <summary>Set only for a Bank account.</summary>
    public BankTermsResponse? Bank { get; init; }
}

public static class AccountMappingExtensions
{
    public static AccountResponse ToResponse(this Account account, Asset cash) => new()
    {
        Id = account.Id,
        Name = account.Name,
        Type = account.Type,
        Wrapper = account.Wrapper,
        Currency = account.Currency,
        IsArchived = account.IsArchived,
        CashAssetId = cash.Id,
        GroupId = account.Type == AccountType.Bank ? cash.GroupId : account.DefaultGroupId,
        CashBalance = cash.Quantity
    };
}
