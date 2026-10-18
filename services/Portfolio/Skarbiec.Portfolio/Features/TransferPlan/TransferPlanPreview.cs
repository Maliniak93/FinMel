using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Planning.Allocation;
using Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;
using Skarbiec.Portfolio.Features.Planning.Recurrence;
using StoredTransferPlan = Skarbiec.Portfolio.Data.TransferPlan;

namespace Skarbiec.Portfolio.Features.TransferPlan;

internal static class TransferPlanPreview
{
    private const decimal MinimumDifference = 0.01m;

    public static TransferPlanResponse Build(
        YearMonth viewed,
        YearMonth current,
        YearMonth? start,
        AllocationResponse allocation,
        IReadOnlyDictionary<Guid, Account> accounts,
        TargetAllocationReplica? target,
        ValuationReplica? valuation,
        int minimum,
        IReadOnlyList<StoredTransferPlan> plans)
    {
        var stored = plans.FirstOrDefault(p => p.Month == viewed.FirstDay);
        if (stored is { IsSkipped: true })
        {
            return Empty(TransferPlanStatus.Skipped, null, minimum, allocation.ToInvestPln);
        }

        TransferPlanNoneReason? noneReason =
            stored is not null ? null
            : start is { } first && viewed < first ? TransferPlanNoneReason.BeforeStart
            : viewed > current ? TransferPlanNoneReason.FutureMonth
            : allocation.ToInvestPln <= 0m ? TransferPlanNoneReason.NothingToInvest
            : null;
        if (noneReason is not null)
        {
            return Empty(TransferPlanStatus.None, noneReason, minimum, allocation.ToInvestPln);
        }

        var groups = (target?.Groups ?? [])
            .Select(g => new TransferGroup(g.Name, g.AssetClasses, g.TargetPercent))
            .ToList();
        var groupValues = groups.ToDictionary(
            g => g.Name,
            g => (valuation?.ByAssetClass ?? []).Where(v => g.AssetClasses.Contains(v.AssetClass)).Sum(v => v.ValuePln));

        var otherRows = plans
            .Where(p => p.Month != viewed.FirstDay && !p.IsSkipped)
            .SelectMany(p => p.Batches)
            .SelectMany(b => b.Rows)
            .Where(r => r.CheckedAtUtc is null);
        foreach (var row in otherRows)
        {
            if (GroupOf(row.AssetClasses, groups) is { } index)
            {
                groupValues[groups[index].Name] += row.AmountPln;
            }
        }

        return stored is null
            ? BuildPreview(allocation, accounts, target, valuation, minimum, groups, groupValues)
            : BuildFrozen(stored, allocation, accounts, target, valuation, minimum, groups, groupValues);
    }

    // The group holding the lowest class: stable when the user renames, merges or reorders groups.
    internal static int? GroupOf(IReadOnlyCollection<AssetClass> assetClasses, IReadOnlyList<TransferGroup> groups)
    {
        if (assetClasses.Count == 0)
        {
            return null;
        }

        var lowest = assetClasses.Min();
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].AssetClasses.Contains(lowest))
            {
                return i;
            }
        }

        return null;
    }

    private static TransferPlanResponse Empty(
        TransferPlanStatus status, TransferPlanNoneReason? noneReason, int minimum, decimal toInvest) =>
        new()
        {
            Status = status,
            NoneReason = noneReason,
            MinimumTransferPln = minimum,
            ValuationDate = null,
            ValuationStale = false,
            FallbackReason = null,
            Rows = [],
            Groups = [],
            ToInvestPln = toInvest,
            CanUndo = false,
            Differences = [],
            TopUp = null
        };

    private sealed record Distribution(
        TransferPlanFallbackReason? FallbackReason, List<TransferRowResponse> Rows, IReadOnlyList<TransferGroupShare> Groups);

    private static Distribution Distribute(
        List<TransferBasket> baskets,
        TargetAllocationReplica? target,
        ValuationReplica? valuation,
        int minimum,
        List<TransferGroup> groups,
        Dictionary<string, decimal> groupValues)
    {
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

        return new Distribution(
            fallbackReason,
            rows
                .Select(r => new TransferRowResponse
                {
                    Id = null,
                    BatchNumber = null,
                    Checked = null,
                    BucketId = r.BucketId,
                    BucketName = basketsById[r.BucketId].BucketName,
                    AccountId = r.AccountId,
                    AccountName = basketsById[r.BucketId].AccountName,
                    GroupName = r.GroupName,
                    AmountPln = r.Amount,
                    Marker = r.Marker
                })
                .ToList(),
            shares);
    }

    private static TransferBasket BasketOf(
        AllocationBucketResponse bucket, int order, decimal amount, IReadOnlyDictionary<Guid, Account> accounts)
    {
        var account = bucket.AccountId is { } id && accounts.TryGetValue(id, out var found) ? found : null;
        return new TransferBasket(bucket.BucketId, bucket.Name, order, account?.Id, account?.Name, account?.Type, amount);
    }

    private static bool Invests(AllocationBucketResponse bucket) =>
        bucket.Kind == BucketKind.Investments || bucket.Kind.IsLimited();

    private static TransferPlanResponse BuildPreview(
        AllocationResponse allocation,
        IReadOnlyDictionary<Guid, Account> accounts,
        TargetAllocationReplica? target,
        ValuationReplica? valuation,
        int minimum,
        List<TransferGroup> groups,
        Dictionary<string, decimal> groupValues)
    {
        var baskets = allocation.Buckets
            .Where(b => Invests(b) && b.AllocatedPln > 0m)
            .Select((b, order) => BasketOf(b, order, b.AllocatedPln, accounts))
            .ToList();

        var distribution = Distribute(baskets, target, valuation, minimum, groups, groupValues);

        return new TransferPlanResponse
        {
            Status = TransferPlanStatus.Preview,
            NoneReason = null,
            MinimumTransferPln = minimum,
            ValuationDate = valuation?.Date,
            ValuationStale = valuation?.IsStale ?? false,
            FallbackReason = distribution.FallbackReason,
            Rows = distribution.Rows,
            Groups = distribution.Groups,
            ToInvestPln = allocation.ToInvestPln,
            CanUndo = false,
            Differences = [],
            TopUp = null
        };
    }

    private static TransferPlanResponse BuildFrozen(
        StoredTransferPlan stored,
        AllocationResponse allocation,
        IReadOnlyDictionary<Guid, Account> accounts,
        TargetAllocationReplica? target,
        ValuationReplica? valuation,
        int minimum,
        List<TransferGroup> groups,
        Dictionary<string, decimal> groupValues)
    {
        var batches = stored.Batches.OrderBy(b => b.Number).ToList();
        var rows = batches
            .SelectMany(b => b.Rows.OrderBy(r => r.Position).Select(r => (Batch: b, Row: r)))
            .ToList();

        var start = groups.Select(g => groupValues[g.Name]).ToArray();
        var values = (decimal[])start.Clone();
        var open = rows.Where(x => x.Row.CheckedAtUtc is null).Select(x => x.Row).ToList();
        foreach (var row in open)
        {
            if (GroupOf(row.AssetClasses, groups) is { } index)
            {
                values[index] += row.AmountPln;
            }
        }

        var startTotal = start.Sum();
        var baseAfter = startTotal + open.Sum(r => r.AmountPln);
        var shares = groups
            .Select((g, i) => new TransferGroupShare(
                g.Name,
                TransferPlanCalculator.Percent(start[i], startTotal),
                TransferPlanCalculator.Percent(values[i], baseAfter),
                g.TargetPercent))
            .ToList();

        var first = batches[0];
        var latest = batches[^1];

        var (differences, topUpBaskets) = DifferencesOf(rows.Select(x => x.Row).ToList(), allocation, accounts);
        TransferTopUpResponse? topUp = null;
        if (topUpBaskets.Count > 0)
        {
            var openValues = groups.Select((g, i) => (g.Name, Value: values[i])).ToDictionary(x => x.Name, x => x.Value);
            var distribution = Distribute(topUpBaskets, target, valuation, minimum, groups, openValues);
            topUp = new TransferTopUpResponse
            {
                MinimumTransferPln = minimum,
                ValuationDate = valuation?.Date,
                FallbackReason = distribution.FallbackReason,
                Rows = distribution.Rows,
                Groups = distribution.Groups
            };
        }

        return new TransferPlanResponse
        {
            Status = rows.Count > 0 && open.Count == 0 ? TransferPlanStatus.Completed : TransferPlanStatus.Accepted,
            NoneReason = null,
            MinimumTransferPln = first.MinimumTransferPln,
            ValuationDate = first.ValuationDate,
            ValuationStale = false,
            FallbackReason = null,
            Rows = rows
                .Select(x => new TransferRowResponse
                {
                    Id = x.Row.Id,
                    BatchNumber = x.Batch.Number,
                    Checked = x.Row.CheckedAtUtc is not null,
                    BucketId = x.Row.BucketId,
                    BucketName = x.Row.BucketName,
                    AccountId = x.Row.AccountId,
                    AccountName = x.Row.AccountName,
                    GroupName = x.Row.GroupName,
                    AmountPln = x.Row.AmountPln,
                    Marker = x.Row.Marker
                })
                .ToList(),
            Groups = shares,
            ToInvestPln = rows.Sum(x => x.Row.AmountPln),
            CanUndo = latest.Rows.All(r => r.CheckedAtUtc is null),
            Differences = differences,
            TopUp = topUp
        };
    }

    private static (List<TransferDifferenceResponse> Differences, List<TransferBasket> TopUpBaskets) DifferencesOf(
        IReadOnlyList<TransferPlanRow> rows, AllocationResponse allocation, IReadOnlyDictionary<Guid, Account> accounts)
    {
        var accepted = rows
            .GroupBy(r => r.BucketId)
            .ToDictionary(g => g.Key, g => (Name: g.First().BucketName, Amount: g.Sum(r => r.AmountPln)));
        var investing = allocation.Buckets.Where(Invests).ToList();

        var entries = investing
            .Select(b => (b.BucketId, b.Name, Current: b.AllocatedPln, Bucket: (AllocationBucketResponse?)b))
            .Concat(accepted
                .Where(a => investing.All(b => b.BucketId != a.Key))
                .Select(a => (BucketId: a.Key, Name: a.Value.Name, Current: 0m, Bucket: (AllocationBucketResponse?)null)))
            .ToList();

        var differences = new List<TransferDifferenceResponse>();
        var baskets = new List<TransferBasket>();
        foreach (var entry in entries)
        {
            var acceptedPln = accepted.TryGetValue(entry.BucketId, out var found) ? found.Amount : 0m;
            var difference = entry.Current - acceptedPln;
            if (Math.Abs(difference) < MinimumDifference)
            {
                continue;
            }

            differences.Add(new TransferDifferenceResponse
            {
                BucketId = entry.BucketId,
                BucketName = entry.Name,
                AcceptedPln = acceptedPln,
                CurrentPln = entry.Current,
                DifferencePln = difference
            });

            if (difference > 0m && entry.Bucket is { } bucket)
            {
                baskets.Add(BasketOf(bucket, baskets.Count, difference, accounts));
            }
        }

        return (differences, baskets);
    }
}
