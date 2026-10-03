using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Bonds;

public enum BondStatus
{
    Active,

    InterestDue,

    Matured,
}

public enum BondPeriodState
{
    Upcoming,

    Due,
}

public sealed record BondPeriodResponse
{
    /// <summary>1-based.</summary>
    public required int Index { get; init; }
    public required DateOnly Start { get; init; }
    public required DateOnly End { get; init; }
    public required BondPeriodState State { get; init; }
}

public sealed record BondResponse
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required string PortfolioName { get; init; }
    public required bool PortfolioIsArchived { get; init; }

    public required bool IsArchived { get; init; }

    public required string Name { get; init; }
    public required string SeriesCode { get; init; }
    public required TreasuryBondType Type { get; init; }
    public required DateOnly PurchaseDate { get; init; }
    public required int BondCount { get; init; }

    /// <summary>PLN per bond of 100 PLN nominal.</summary>
    public required decimal PurchasePricePerBond { get; init; }

    public required decimal FirstPeriodRatePercent { get; init; }
    public decimal? MarginPercent { get; init; }

    /// <summary>PLN per bond.</summary>
    public required decimal EarlyRedemptionFeePerBond { get; init; }

    public required bool TaxExempt { get; init; }
    public required DateOnly MaturityDate { get; init; }

    /// <summary>Bond count × 100 PLN.</summary>
    public required decimal NominalValue { get; init; }

    /// <summary>The asset's quantity in PLN; starts at the purchase cost.</summary>
    public required decimal BookValue { get; init; }

    /// <summary>The Cash asset the purchase was paid from; null for new money or once the transfer was detached.</summary>
    public Guid? FundingAssetId { get; init; }

    public string? FundingAssetName { get; init; }

    public required BondStatus Status { get; init; }
    public required IReadOnlyList<BondPeriodResponse> Periods { get; init; }
}

public static class BondMappingExtensions
{
    public const decimal NominalPerBond = 100m;

    public static BondResponse ToResponse(
        this TreasuryBond terms,
        Asset asset,
        string portfolioName,
        bool portfolioIsArchived,
        DateOnly today,
        DepositFundingSource? funding)
    {
        var periods = BondSchedule.Periods(terms.Type, terms.PurchaseDate)
            .Select(p => new BondPeriodResponse
            {
                Index = p.Index,
                Start = p.Start,
                End = p.End,
                State = p.End <= today ? BondPeriodState.Due : BondPeriodState.Upcoming
            })
            .ToList();

        return new BondResponse
        {
            AssetId = asset.Id,
            PortfolioId = asset.PortfolioId,
            PortfolioName = portfolioName,
            PortfolioIsArchived = portfolioIsArchived,
            IsArchived = asset.IsArchived,
            Name = asset.Name,
            SeriesCode = terms.SeriesCode,
            Type = terms.Type,
            PurchaseDate = terms.PurchaseDate,
            BondCount = terms.BondCount,
            PurchasePricePerBond = terms.PurchasePricePerBond,
            FirstPeriodRatePercent = terms.FirstPeriodRatePercent,
            MarginPercent = terms.MarginPercent,
            EarlyRedemptionFeePerBond = terms.EarlyRedemptionFeePerBond,
            TaxExempt = terms.TaxExempt,
            MaturityDate = terms.MaturityDate,
            NominalValue = terms.BondCount * NominalPerBond,
            BookValue = asset.Quantity,
            FundingAssetId = funding?.AssetId,
            FundingAssetName = funding?.AssetName,
            Status = terms.MaturityDate <= today ? BondStatus.Matured
                : periods.Any(p => p.State == BondPeriodState.Due) ? BondStatus.InterestDue
                : BondStatus.Active,
            Periods = periods
        };
    }
}
