using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features;

public sealed record AssetResponse
{
    public required Guid Id { get; init; }
    public Guid? AccountId { get; init; }
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

        return asset.ToResponse(transactionCount, terms?.MaturityDate, terms?.Settled, fineWeight);
    }
}
