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

    /// <summary>The account's group, inherited by its money asset and every component without an exception.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>The account's money in its currency, the quantity of its cash asset.</summary>
    public required decimal CashBalance { get; init; }

    /// <summary>True when the Bank account is kept outside net worth.</summary>
    public bool OutsideNetWorth { get; init; }

    /// <summary>Set only by the account reads, for an IKE or IKZE account; null for a Regular one.</summary>
    public ContributionResponse? Contribution { get; init; }

    /// <summary>Set only by the account reads, for a Brokerage or Bonds account: external deposits less withdrawals up to today, PLN.</summary>
    public decimal? NetContributionsPln { get; init; }

    /// <summary>Set only by the account reads, for a Brokerage or Bonds account: purchase cost of the open positions, PLN; null when a position has no PLN cost.</summary>
    public decimal? PositionsCostPln { get; init; }

    /// <summary>Set only by the account reads, for a Brokerage or Bonds account: gross coupons paid on the open bonds, PLN.</summary>
    public decimal? PaidInterestPln { get; init; }

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
        GroupId = account.GroupId,
        CashBalance = cash.Quantity,
        OutsideNetWorth = account.OutsideNetWorth
    };
}
