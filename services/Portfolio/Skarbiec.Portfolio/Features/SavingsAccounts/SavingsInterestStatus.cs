using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.SavingsAccounts;

/// <summary>A stored <see cref="SavingsInterestSettlement"/> on the wire (savings-interest-settlement).</summary>
public sealed record SavingsInterestSettlementResponse
{
    public required Guid SettlementId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
}

/// <summary>A savings account's interest state at read time: the next due period, if any, and its latest settlement.</summary>
public sealed record SavingsInterestStatus(SavingsInterestDuePeriod? Due, SavingsInterestSettlementResponse? LastSettlement)
{
    public static readonly SavingsInterestStatus None = new(null, null);
}

public static class SavingsInterestStatusExtensions
{
    public static SavingsInterestSettlementResponse ToResponse(this SavingsInterestSettlement settlement) => new()
    {
        SettlementId = settlement.Id,
        PeriodStart = settlement.PeriodStart,
        PeriodEnd = settlement.PeriodEnd,
        GrossInterest = settlement.GrossInterest,
        Tax = settlement.Tax,
        NetInterest = settlement.GrossInterest - settlement.Tax
    };

    /// <summary>
    /// The interest status of each savings account in <paramref name="assetIds"/> against
    /// <paramref name="today"/> (Europe/Warsaw), in three queries whatever the count — the read models
    /// (savings accounts, assets) compute it at read time, nothing is stored. An id with no
    /// <see cref="SavingsAccount"/> row is absent from the result.
    /// </summary>
    internal static async Task<IReadOnlyDictionary<Guid, SavingsInterestStatus>> LoadSavingsInterestStatusAsync(
        this PortfolioDbContext dbContext, IReadOnlyCollection<Guid> assetIds, DateOnly today, CancellationToken cancellationToken)
    {
        if (assetIds.Count == 0)
        {
            return new Dictionary<Guid, SavingsInterestStatus>();
        }

        var terms = await dbContext.SavingsAccounts
            .AsNoTracking()
            .Where(s => assetIds.Contains(s.AssetId))
            .ToListAsync(cancellationToken);

        var transactions = (await dbContext.Transactions
                .AsNoTracking()
                .Where(t => assetIds.Contains(t.AssetId))
                .ToListAsync(cancellationToken))
            .ToLookup(t => t.AssetId);

        // At most one settlement per account per month, so loading them all stays small.
        var latestSettlements = (await dbContext.SavingsInterestSettlements
                .AsNoTracking()
                .Where(s => assetIds.Contains(s.AssetId))
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.AssetId)
            .ToDictionary(g => g.Key, g => g.MaxBy(s => s.PeriodEnd)!);

        return terms.ToDictionary(
            t => t.AssetId,
            t =>
            {
                var latest = latestSettlements.GetValueOrDefault(t.AssetId);
                var due = SavingsInterestMath.NextDuePeriod(
                    [.. transactions[t.AssetId].Select(SavingsCashFlow.From)],
                    t.AnnualInterestRatePercent,
                    t.TaxExempt,
                    latest?.PeriodEnd,
                    today);

                return new SavingsInterestStatus(due, latest?.ToResponse());
            });
    }

    /// <summary><see cref="LoadSavingsInterestStatusAsync(PortfolioDbContext, IReadOnlyCollection{Guid}, DateOnly, CancellationToken)"/> for one account.</summary>
    internal static async Task<SavingsInterestStatus> LoadSavingsInterestStatusAsync(
        this PortfolioDbContext dbContext, Guid assetId, DateOnly today, CancellationToken cancellationToken)
        => (await dbContext.LoadSavingsInterestStatusAsync([assetId], today, cancellationToken))
            .GetValueOrDefault(assetId, SavingsInterestStatus.None);
}
