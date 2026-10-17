using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Allocation;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.Planning.Recurrence;

namespace Skarbiec.Portfolio.Features.TransferPlan;

internal static class TransferPlanPreview
{
    public static TransferPlanResponse Build(
        YearMonth viewed,
        YearMonth current,
        YearMonth? start,
        AllocationResponse allocation,
        IReadOnlyDictionary<Guid, Account> accounts,
        TargetAllocationReplica? target,
        ValuationReplica? valuation,
        int minimum)
    {
        TransferPlanNoneReason? noneReason =
            start is { } first && viewed < first ? TransferPlanNoneReason.BeforeStart
            : viewed > current ? TransferPlanNoneReason.FutureMonth
            : allocation.ToInvestPln <= 0m ? TransferPlanNoneReason.NothingToInvest
            : null;
        if (noneReason is not null)
        {
            return new TransferPlanResponse
            {
                Status = TransferPlanStatus.None,
                NoneReason = noneReason,
                MinimumTransferPln = minimum,
                ValuationDate = null,
                ValuationStale = false,
                FallbackReason = null,
                Rows = [],
                Groups = [],
                ToInvestPln = allocation.ToInvestPln
            };
        }

        var baskets = allocation.Buckets
            .Where(b => (b.Kind == BucketKind.Investments || b.Kind.IsLimited()) && b.AllocatedPln > 0m)
            .Select((b, order) =>
            {
                var account = b.AccountId is { } id && accounts.TryGetValue(id, out var found) ? found : null;
                return new TransferBasket(b.BucketId, b.Name, order, account?.Id, account?.Name, account?.Type, b.AllocatedPln);
            })
            .ToList();

        var groups = (target?.Groups ?? [])
            .Select(g => new TransferGroup(g.Name, g.AssetClasses, g.TargetPercent))
            .ToList();
        var groupValues = groups.ToDictionary(
            g => g.Name,
            g => (valuation?.ByAssetClass ?? []).Where(v => g.AssetClasses.Contains(v.AssetClass)).Sum(v => v.ValuePln));

        TransferPlanFallbackReason? fallbackReason =
            target is null ? TransferPlanFallbackReason.NoTargetAllocation
            : valuation is null ? TransferPlanFallbackReason.NoValuation
            : groupValues.Values.Sum() <= 0m ? TransferPlanFallbackReason.EmptyBase
            : null;

        var basketsById = baskets.ToDictionary(b => b.BucketId);
        IReadOnlyList<TransferRow> rows;
        IReadOnlyList<TransferGroupShare> shares = [];
        if (fallbackReason is null)
        {
            var result = TransferPlanCalculator.Compute(baskets, groups, groupValues, minimum);
            rows = result.Rows;
            shares = result.Groups;
        }
        else
        {
            rows = baskets
                .Select(b => new TransferRow(
                    b.BucketId, b.AccountId, null, b.Amount, b.Amount < minimum ? TransferMarker.BelowMinimum : TransferMarker.None))
                .ToList();
        }

        return new TransferPlanResponse
        {
            Status = TransferPlanStatus.Preview,
            NoneReason = null,
            MinimumTransferPln = minimum,
            ValuationDate = valuation?.Date,
            ValuationStale = valuation?.IsStale ?? false,
            FallbackReason = fallbackReason,
            Rows = rows
                .Select(r => new TransferRowResponse
                {
                    BucketId = r.BucketId,
                    BucketName = basketsById[r.BucketId].BucketName,
                    AccountId = r.AccountId,
                    AccountName = basketsById[r.BucketId].AccountName,
                    GroupName = r.GroupName,
                    AmountPln = r.Amount,
                    Marker = r.Marker
                })
                .ToList(),
            Groups = shares,
            ToInvestPln = allocation.ToInvestPln
        };
    }
}
