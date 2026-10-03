using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits;

public enum DepositStatus
{
    Active,

    Due,

    Settled,

    PaidOut,
}

public sealed record DepositProjectionResponse
{
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
    public required decimal FinalAmount { get; init; }
    public required decimal NetProfitPercent { get; init; }
}

public sealed record DepositResponse
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required string PortfolioName { get; init; }
    public required bool PortfolioIsArchived { get; init; }

    public required bool IsArchived { get; init; }

    public required string Name { get; init; }
    public string? BankName { get; init; }
    public required string Currency { get; init; }
    public required decimal Principal { get; init; }
    public required DateOnly StartDate { get; init; }
    public required int TermLength { get; init; }
    public required DepositTermUnit TermUnit { get; init; }
    public required DateOnly MaturityDate { get; init; }
    public required decimal AnnualInterestRatePercent { get; init; }
    public required DepositCapitalization Capitalization { get; init; }
    public required bool TaxExempt { get; init; }
    public required decimal EarlyBreakInterestLossPercent { get; init; }
    public required DepositProjectionResponse Projection { get; init; }
    public required DepositStatus Status { get; init; }

    public DateOnly? SettledOn { get; init; }

    public decimal? SettledGrossInterest { get; init; }
    public decimal? SettledTax { get; init; }

    /// <summary>The Cash asset the principal came from; null when the money came from outside the app or the transfer was detached.</summary>
    public Guid? FundingAssetId { get; init; }

    public string? FundingAssetName { get; init; }

    public DateOnly? PaidOutOn { get; init; }

    /// <summary>Null until the deposit is paid out, and again once the destination asset was removed.</summary>
    public string? PaidOutToAssetName { get; init; }

    public required int RolloverCount { get; init; }
}

public static class DepositMappingExtensions
{
    public static DepositResponse ToResponse(
        this TermDeposit terms,
        Asset asset,
        string portfolioName,
        bool portfolioIsArchived,
        DateOnly today,
        DepositFundingSource? funding,
        DepositPayoutInfo? payout)
    {
        var projection = DepositInterestMath.Project(new DepositTerms
        {
            Principal = terms.Principal,
            StartDate = terms.StartDate,
            TermLength = terms.TermLength,
            TermUnit = terms.TermUnit,
            AnnualInterestRatePercent = terms.AnnualInterestRatePercent,
            Capitalization = terms.Capitalization,
            TaxExempt = terms.TaxExempt
        });

        return new DepositResponse
        {
            AssetId = asset.Id,
            PortfolioId = asset.PortfolioId,
            PortfolioName = portfolioName,
            PortfolioIsArchived = portfolioIsArchived,
            IsArchived = asset.IsArchived,
            Name = asset.Name,
            BankName = terms.BankName,
            Currency = asset.Currency,
            Principal = terms.Principal,
            StartDate = terms.StartDate,
            TermLength = terms.TermLength,
            TermUnit = terms.TermUnit,
            MaturityDate = terms.MaturityDate,
            AnnualInterestRatePercent = terms.AnnualInterestRatePercent,
            Capitalization = terms.Capitalization,
            TaxExempt = terms.TaxExempt,
            EarlyBreakInterestLossPercent = terms.EarlyBreakInterestLossPercent,
            Projection = new DepositProjectionResponse
            {
                GrossInterest = projection.GrossInterest,
                Tax = projection.Tax,
                NetInterest = projection.NetInterest,
                FinalAmount = projection.FinalAmount,
                NetProfitPercent = projection.NetProfitPercent
            },
            Status = terms.SettledOn is not null
                ? payout is not null ? DepositStatus.PaidOut : DepositStatus.Settled
                : terms.MaturityDate <= today ? DepositStatus.Due : DepositStatus.Active,
            SettledOn = terms.SettledOn,
            SettledGrossInterest = terms.SettledGrossInterest,
            SettledTax = terms.SettledTax,
            FundingAssetId = funding?.AssetId,
            FundingAssetName = funding?.AssetName,
            PaidOutOn = payout?.PaidOutOn,
            PaidOutToAssetName = payout?.DestinationAssetName,
            RolloverCount = terms.RolloverCount
        };
    }
}
