using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.TransferPlan;

internal static class TransferPlanBatchFactory
{
    public static async Task<TransferPlanBatch> CreateAsync(
        PortfolioDbContext dbContext,
        Guid planId,
        int number,
        IReadOnlyList<TransferRowResponse> rows,
        DateOnly? valuationDate,
        int minimumTransferPln,
        DateTimeOffset acceptedAtUtc,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.TargetAllocationReplicas.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var accountIds = rows.Where(r => r.AccountId is not null).Select(r => r.AccountId!.Value).Distinct().ToList();
        var accountTypes = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Type, cancellationToken);

        var batchId = Guid.NewGuid();

        return new TransferPlanBatch
        {
            Id = batchId,
            PlanId = planId,
            Number = number,
            AcceptedAtUtc = acceptedAtUtc,
            ValuationDate = valuationDate,
            MinimumTransferPln = minimumTransferPln,
            Rows = rows
                .Select((row, position) => new TransferPlanRow
                {
                    Id = Guid.NewGuid(),
                    BatchId = batchId,
                    BucketId = row.BucketId,
                    BucketName = row.BucketName,
                    AccountId = row.AccountId,
                    AccountName = row.AccountName,
                    GroupName = row.GroupName,
                    AssetClasses = ClassesOf(row, target, accountTypes),
                    AmountPln = row.AmountPln,
                    Marker = row.Marker,
                    Position = position
                })
                .ToList()
        };
    }

    private static List<AssetClass> ClassesOf(
        TransferRowResponse row, TargetAllocationReplica? target, Dictionary<Guid, AccountType> accountTypes)
    {
        var group = row.GroupName is null ? null : target?.Groups.FirstOrDefault(g => g.Name == row.GroupName);
        if (group is null)
        {
            return [];
        }

        var buyable = row.AccountId is { } id && accountTypes.TryGetValue(id, out var type)
            ? TransferPlanCalculator.BuyableClasses(type)
            : null;

        return group.AssetClasses.Where(c => buyable is null || buyable.Contains(c)).ToList();
    }
}
