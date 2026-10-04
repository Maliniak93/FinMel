using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits;

public sealed record DepositFundingSource(Guid AssetId, string AssetName);

internal static class DepositFunding
{
    public static async Task<IReadOnlyDictionary<Guid, DepositFundingSource>> LoadFundingSourcesAsync(
        this PortfolioDbContext dbContext, IReadOnlyCollection<Guid> depositIds, CancellationToken cancellationToken)
    {
        // A plain list parameter, whatever collection the caller passed.
        var ids = depositIds.ToList();

        var rows = await (
                from inLeg in dbContext.Transactions.AsNoTracking()
                where ids.Contains(inLeg.AssetId) && inLeg.TransferId != null && inLeg.Type == TransactionType.Deposit
                join outLeg in dbContext.Transactions on inLeg.TransferId equals outLeg.TransferId
                where outLeg.Id != inLeg.Id
                join source in dbContext.Assets on outLeg.AssetId equals source.Id
                // A swap-born bond's opening is the In leg of a Bond → Bond transfer, shown as swappedFrom instead.
                where source.AssetClass == AssetClass.Cash
                select new { DepositId = inLeg.AssetId, SourceId = source.Id, SourceName = source.Name })
            .ToListAsync(cancellationToken);

        return rows
            .DistinctBy(r => r.DepositId)
            .ToDictionary(r => r.DepositId, r => new DepositFundingSource(r.SourceId, r.SourceName));
    }

    public static async Task<DepositFundingSource?> LoadFundingSourceAsync(
        this PortfolioDbContext dbContext, Guid depositId, CancellationToken cancellationToken)
        => (await dbContext.LoadFundingSourcesAsync([depositId], cancellationToken)).GetValueOrDefault(depositId);
}
