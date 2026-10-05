using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Deposits;

namespace Skarbiec.Portfolio.Features.Bonds;

public enum BondStatus
{
    Active,

    InterestDue,

    Matured,

    Redeemed,
}

public enum BondPeriodState
{
    Upcoming,

    Due,

    Settled,
}

public sealed record BondSettlementResponse
{
    public required Guid SettlementId { get; init; }
    public required decimal RatePercent { get; init; }
    public required int BondCount { get; init; }

    /// <summary>PLN for all the bonds of the lot.</summary>
    public required decimal GrossInterest { get; init; }

    /// <summary>Belka tax withheld, PLN; 0 for capitalised interest, which is taxed at redemption.</summary>
    public required decimal Tax { get; init; }
}

public sealed record BondPeriodResponse
{
    /// <summary>1-based.</summary>
    public required int Index { get; init; }
    public required DateOnly Start { get; init; }
    public required DateOnly End { get; init; }
    public required BondPeriodState State { get; init; }

    /// <summary>Set only on a Settled period.</summary>
    public BondSettlementResponse? Settlement { get; init; }
}

public sealed record BondRedemptionResponse
{
    public required BondRedemptionKind Kind { get; init; }
    public required DateOnly Date { get; init; }

    /// <summary>The whole holding — a swap redeems every bond, swapped or not.</summary>
    public required int BondCount { get; init; }

    /// <summary>Belka tax on the capitalised interest and the purchase discount, PLN.</summary>
    public required decimal Tax { get; init; }

    /// <summary>What the redemption pays out after tax, PLN; a swap spends part of it on the new bond.</summary>
    public required decimal Proceeds { get; init; }

    /// <summary>The Cash asset that received the proceeds or a swap's leftover; null when nothing was paid to Cash or once it was removed.</summary>
    public string? DestinationAssetName { get; init; }

    /// <summary>The bond bought in a swap; null for a maturity redemption or once it was removed.</summary>
    public string? SwapTargetAssetName { get; init; }
}

public sealed record BondSwapSourceResponse
{
    public required Guid AssetId { get; init; }
    public required string Name { get; init; }
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

    /// <summary>Ended periods not settled yet.</summary>
    public required int DuePeriodCount { get; init; }

    /// <summary>The settlement with the highest period index — the only one that can be undone.</summary>
    public BondSettlementResponse? LastSettlement { get; init; }

    public required IReadOnlyList<BondPeriodResponse> Periods { get; init; }

    public required IReadOnlyList<BondRedemptionResponse> Redemptions { get; init; }

    /// <summary>The matured bond this one was bought with in a swap; null for a purchase or once that bond was removed.</summary>
    public BondSwapSourceResponse? SwappedFrom { get; init; }
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
        DepositFundingSource? funding,
        IReadOnlyCollection<BondInterestSettlement> settlements,
        IReadOnlyList<BondRedemptionResponse> redemptions,
        BondSwapSourceResponse? swappedFrom)
    {
        var settled = settlements.ToDictionary(s => s.PeriodIndex, s => s.ToResponse());
        var periods = BondSchedule.Periods(terms.Type, terms.PurchaseDate)
            .Select(p => new BondPeriodResponse
            {
                Index = p.Index,
                Start = p.Start,
                End = p.End,
                State = settled.ContainsKey(p.Index) ? BondPeriodState.Settled
                    : p.End <= today ? BondPeriodState.Due
                    : BondPeriodState.Upcoming,
                Settlement = settled.GetValueOrDefault(p.Index)
            })
            .ToList();
        var duePeriodCount = periods.Count(p => p.State == BondPeriodState.Due);
        var lastSettlement = settlements.MaxBy(s => s.PeriodIndex);

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
            // A holding is only ever redeemed whole, so one redemption means nothing is left.
            Status = redemptions.Count > 0 ? BondStatus.Redeemed
                : terms.MaturityDate <= today ? BondStatus.Matured
                : duePeriodCount > 0 ? BondStatus.InterestDue
                : BondStatus.Active,
            DuePeriodCount = duePeriodCount,
            LastSettlement = lastSettlement?.ToResponse(),
            Periods = periods,
            Redemptions = redemptions,
            SwappedFrom = swappedFrom
        };
    }

    public static BondSettlementResponse ToResponse(this BondInterestSettlement settlement) => new()
    {
        SettlementId = settlement.Id,
        RatePercent = settlement.RatePercent,
        BondCount = settlement.BondCount,
        GrossInterest = settlement.GrossInterest,
        Tax = settlement.Tax
    };
}
