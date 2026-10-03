using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.SavingsAccounts;

namespace Skarbiec.Portfolio.Features;

public sealed record AssetResponse
{
    public required Guid Id { get; init; }
    public required Guid PortfolioId { get; init; }
    public required AssetClass AssetClass { get; init; }
    public required AssetValuationMode ValuationMode { get; init; }
    public required string Name { get; init; }
    public required string Currency { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? ManualValue { get; init; }
    public DateOnly? ManualValueDate { get; init; }
    public Guid? InstrumentId { get; init; }

    public required int TransactionCount { get; init; }

    public DateOnly? DepositMaturityDate { get; init; }

    public bool? DepositSettled { get; init; }

    public bool? SavingsInterestDue { get; init; }

    public required bool IsArchived { get; init; }
}

public static class AssetMappingExtensions
{
    public static AssetResponse ToResponse(
        this Asset asset,
        int transactionCount,
        DateOnly? depositMaturityDate = null,
        bool? depositSettled = null,
        bool? savingsInterestDue = null) => new()
        {
            Id = asset.Id,
            PortfolioId = asset.PortfolioId,
            AssetClass = asset.AssetClass,
            ValuationMode = asset.ValuationMode,
            Name = asset.Name,
            Currency = asset.Currency,
            Quantity = asset.Quantity,
            ManualValue = asset.ManualValueAmount,
            ManualValueDate = asset.ManualValueDate,
            InstrumentId = asset.InstrumentId,
            TransactionCount = transactionCount,
            DepositMaturityDate = depositMaturityDate,
            DepositSettled = depositSettled,
            SavingsInterestDue = savingsInterestDue,
            IsArchived = asset.IsArchived
        };

    internal static async Task<AssetResponse> ToFullResponseAsync(
        this PortfolioDbContext dbContext, Asset asset, DateOnly today, CancellationToken cancellationToken)
    {
        var transactionCount = await dbContext.Transactions.CountAsync(t => t.AssetId == asset.Id, cancellationToken);
        var terms = await dbContext.TermDeposits
            .AsNoTracking()
            .Where(t => t.AssetId == asset.Id)
            .Select(t => new { t.MaturityDate, Settled = t.SettledOn != null })
            .FirstOrDefaultAsync(cancellationToken);

        var savingsInterestDue = await dbContext.SavingsInterestDueAsync(asset, today, cancellationToken);

        return asset.ToResponse(transactionCount, terms?.MaturityDate, terms?.Settled, savingsInterestDue);
    }

    internal static async Task<bool?> SavingsInterestDueAsync(
        this PortfolioDbContext dbContext, Asset asset, DateOnly today, CancellationToken cancellationToken)
    {
        if (asset.AssetClass != AssetClass.Savings)
        {
            return null;
        }

        var status = await dbContext.LoadSavingsInterestStatusAsync([asset.Id], today, cancellationToken);
        return status.TryGetValue(asset.Id, out var interest) ? interest.Due is not null : null;
    }
}
