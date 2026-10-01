using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.SavingsAccounts;

public sealed record SavingsAccountResponse
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required string PortfolioName { get; init; }
    public required bool PortfolioIsArchived { get; init; }

    /// <summary>The account asset's own archive flag (asset-archive), independent of <see cref="PortfolioIsArchived"/>.</summary>
    public required bool IsArchived { get; init; }

    public required string Name { get; init; }
    public string? BankName { get; init; }
    public required string Currency { get; init; }

    /// <summary><see cref="Asset.Quantity"/> — the sum of the account's Deposit/Withdraw transactions, in <see cref="Currency"/>.</summary>
    public required decimal Balance { get; init; }

    public required decimal AnnualInterestRatePercent { get; init; }
    public required bool TaxExempt { get; init; }

    /// <summary>An ended calendar month with interest is waiting to be settled (savings-interest-settlement).</summary>
    public required bool InterestDue { get; init; }

    /// <summary>How many ended, unsettled months carry interest — 0 when <see cref="InterestDue"/> is false.</summary>
    public required int DuePeriodCount { get; init; }

    /// <summary>The latest settlement — the only one that can be undone; <see langword="null"/> before the first.</summary>
    public SavingsInterestSettlementResponse? LastSettlement { get; init; }
}

public static class SavingsAccountMappingExtensions
{
    public static SavingsAccountResponse ToResponse(
        this SavingsAccount terms, Asset asset, string portfolioName, bool portfolioIsArchived, SavingsInterestStatus interest) => new()
        {
            AssetId = asset.Id,
            PortfolioId = asset.PortfolioId,
            PortfolioName = portfolioName,
            PortfolioIsArchived = portfolioIsArchived,
            IsArchived = asset.IsArchived,
            Name = asset.Name,
            BankName = terms.BankName,
            Currency = asset.Currency,
            Balance = asset.Quantity,
            AnnualInterestRatePercent = terms.AnnualInterestRatePercent,
            TaxExempt = terms.TaxExempt,
            InterestDue = interest.Due is not null,
            DuePeriodCount = interest.Due?.DuePeriodCount ?? 0,
            LastSettlement = interest.LastSettlement
        };
}
