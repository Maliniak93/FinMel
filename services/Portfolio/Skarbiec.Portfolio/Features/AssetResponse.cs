using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Securities;

namespace Skarbiec.Portfolio.Features;

public sealed record AssetResponse
{
    public required Guid Id { get; init; }
    public Guid? AccountId { get; init; }
    public Guid? GroupId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required AssetValuationMode ValuationMode { get; init; }
    public required string Name { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }
    public Guid? InstrumentId { get; init; }

    public required int TransactionCount { get; init; }

    public DateOnly? DepositMaturityDate { get; init; }

    public bool? DepositSettled { get; init; }

    /// <summary>PreciousMetal only: grams of fine metal per piece, the multiplier on the per-gram price.</summary>
    public decimal? FineWeightGramsPerPiece { get; init; }

    public required bool IsArchived { get; init; }

    /// <summary>Stock, ETF, precious metal and bond only (single-asset reads): cost of the open position in PLN; null when a buy had no rate.</summary>
    public decimal? CostPln { get; init; }

    /// <summary>Cost per unit in PLN, fees included; the purchase price for a bond.</summary>
    public decimal? AveragePricePln { get; init; }

    /// <summary>Stock and ETF only: dividends and interest received, PLN.</summary>
    public decimal? DividendPln { get; init; }

    /// <summary>Shares, pieces or bonds held.</summary>
    public decimal? UnitCount { get; init; }
}

public static class AssetMappingExtensions
{
    public static AssetResponse ToResponse(
        this Asset asset,
        int transactionCount,
        DateOnly? depositMaturityDate = null,
        bool? depositSettled = null,
        decimal? fineWeightGramsPerPiece = null) => new()
        {
            Id = asset.Id,
            AccountId = asset.AccountId,
            GroupId = asset.GroupId,
            AssetClass = asset.AssetClass,
            ValuationMode = asset.ValuationMode,
            Name = asset.Name,
            Currency = asset.Currency,
            Quantity = asset.Quantity,
            InstrumentId = asset.InstrumentId,
            TransactionCount = transactionCount,
            DepositMaturityDate = depositMaturityDate,
            DepositSettled = depositSettled,
            FineWeightGramsPerPiece = fineWeightGramsPerPiece,
            IsArchived = asset.IsArchived
        };

    internal static async Task<AssetResponse> ToFullResponseAsync(
        this PortfolioDbContext dbContext, Asset asset, CancellationToken cancellationToken)
    {
        var transactionCount = await dbContext.Transactions.CountAsync(t => t.AssetId == asset.Id, cancellationToken);
        var terms = await dbContext.TermDeposits
            .AsNoTracking()
            .Where(t => t.AssetId == asset.Id)
            .Select(t => new { t.MaturityDate, Settled = t.SettledOn != null })
            .FirstOrDefaultAsync(cancellationToken);

        var fineWeight = await dbContext.MetalHoldings
            .AsNoTracking()
            .Where(m => m.AssetId == asset.Id)
            .Select(m => (decimal?)m.FineWeightGramsPerPiece)
            .FirstOrDefaultAsync(cancellationToken);

        return await dbContext.WithKpisAsync(
            asset.ToResponse(transactionCount, terms?.MaturityDate, terms?.Settled, fineWeight), asset, cancellationToken);
    }

    internal static async Task<AssetResponse> WithKpisAsync(
        this PortfolioDbContext dbContext, AssetResponse response, Asset asset, CancellationToken cancellationToken)
    {
        switch (asset.AssetClass)
        {
            case AssetClass.Stock or AssetClass.Etf or AssetClass.PreciousMetal:
                var transactions = await dbContext.Transactions
                    .AsNoTracking()
                    .Where(t => t.AssetId == asset.Id)
                    .ToListAsync(cancellationToken);
                var basis = SecurityCostBasis.Compute(transactions);

                return response with
                {
                    CostPln = basis.CostPln is { } cost ? Math.Round(cost, 2, MidpointRounding.AwayFromZero) : null,
                    AveragePricePln = basis.AveragePricePln,
                    DividendPln = asset.AssetClass == AssetClass.PreciousMetal
                        ? null
                        : transactions
                            .Where(t => t.Type is TransactionType.Dividend or TransactionType.Interest)
                            .Sum(t => t.Quantity),
                    UnitCount = basis.Quantity
                };

            case AssetClass.Bond:
                var terms = await dbContext.TreasuryBonds
                    .AsNoTracking()
                    .Where(b => b.AssetId == asset.Id)
                    .Select(b => new { b.BondCount, b.PurchasePricePerBond })
                    .FirstOrDefaultAsync(cancellationToken);

                return terms is null
                    ? response
                    : response with
                    {
                        CostPln = asset.Quantity,
                        AveragePricePln = terms.PurchasePricePerBond,
                        UnitCount = terms.BondCount
                    };

            default:
                return response;
        }
    }
}
