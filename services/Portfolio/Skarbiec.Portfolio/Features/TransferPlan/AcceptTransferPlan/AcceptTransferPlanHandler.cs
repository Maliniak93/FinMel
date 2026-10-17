using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.RecurringIncomes.Occurrences;
using StoredTransferPlan = Skarbiec.Portfolio.Data.TransferPlan;

namespace Skarbiec.Portfolio.Features.TransferPlan.AcceptTransferPlan;

public sealed class AcceptTransferPlanHandler(
    PortfolioDbContext dbContext, GetPlanningMonthHandler planningMonth, TimeProvider timeProvider)
{
    public async Task<Result> HandleAsync(string month, CancellationToken cancellationToken)
    {
        var parsed = OccurrenceRules.ParseMonth(month);
        if (parsed.IsFailure)
        {
            return parsed.Error;
        }

        // The server computes the rows itself: a stale client can never freeze numbers it would not produce.
        var planning = await planningMonth.HandleAsync(month, cancellationToken);
        if (planning.IsFailure)
        {
            return planning.Error;
        }

        var preview = planning.Value.TransferPlan;
        if (preview.Status != TransferPlanStatus.Preview)
        {
            return TransferPlanErrors.NotPreview(month);
        }

        var target = await dbContext.TargetAllocationReplicas.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var accountIds = preview.Rows.Where(r => r.AccountId is not null).Select(r => r.AccountId!.Value).Distinct().ToList();
        var accountTypes = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.Type, cancellationToken);

        var batchId = Guid.NewGuid();
        var rows = preview.Rows
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
            .ToList();

        var plan = new StoredTransferPlan { Id = Guid.NewGuid(), Month = parsed.Value.FirstDay, IsSkipped = false };
        plan.Batches.Add(new TransferPlanBatch
        {
            Id = batchId,
            PlanId = plan.Id,
            Number = 1,
            AcceptedAtUtc = timeProvider.GetUtcNow(),
            ValuationDate = preview.ValuationDate,
            MinimumTransferPln = preview.MinimumTransferPln,
            Rows = rows
        });

        dbContext.TransferPlans.Add(plan);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
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
