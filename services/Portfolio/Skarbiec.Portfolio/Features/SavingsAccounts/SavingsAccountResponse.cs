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
}

public static class SavingsAccountMappingExtensions
{
    public static SavingsAccountResponse ToResponse(
        this SavingsAccount terms, Asset asset, string portfolioName, bool portfolioIsArchived) => new()
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
            TaxExempt = terms.TaxExempt
        };
}
