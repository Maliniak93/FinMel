using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.SavingsAccounts;

public sealed record SavingsAccountResponse
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required string PortfolioName { get; init; }
    public required bool PortfolioIsArchived { get; init; }

    public required bool IsArchived { get; init; }

    public required string Name { get; init; }
    public string? BankName { get; init; }
    public required string Currency { get; init; }

    public required decimal Balance { get; init; }

    public required decimal AnnualInterestRatePercent { get; init; }
    public required bool TaxExempt { get; init; }

    public required bool InterestDue { get; init; }

    /// <summary>Ended, unsettled months carrying interest; 0 when InterestDue is false.</summary>
    public required int DuePeriodCount { get; init; }

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
