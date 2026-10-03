using Microsoft.EntityFrameworkCore;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.SavingsAccounts;

public sealed record SavingsInterestSettlementResponse
{
    public required Guid SettlementId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
}

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

    internal static async Task<SavingsInterestStatus> LoadSavingsInterestStatusAsync(
        this PortfolioDbContext dbContext, Guid assetId, DateOnly today, CancellationToken cancellationToken)
        => (await dbContext.LoadSavingsInterestStatusAsync([assetId], today, cancellationToken))
            .GetValueOrDefault(assetId, SavingsInterestStatus.None);
}
