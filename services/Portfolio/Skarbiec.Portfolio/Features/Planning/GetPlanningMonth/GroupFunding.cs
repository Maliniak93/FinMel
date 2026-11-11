using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

internal sealed record FundingLeg(
    Guid? TransferId,
    TransactionType Type,
    DateOnly Date,
    Guid? GroupId,
    Guid? AccountId,
    decimal Quantity,
    decimal UnitPriceAmount,
    decimal? FxRateToPln,
    decimal? AmountPln);

internal sealed record ContributionSource(decimal AmountPln, Guid? AccountId);

internal sealed record GroupRealisation(decimal TotalPln, decimal FromIncomePln, decimal ExcessPln, decimal FundedPln);

internal sealed record GroupFundingResult(IReadOnlyDictionary<Guid, GroupRealisation> ByGroup, bool Incomplete)
{
    public static readonly GroupFundingResult Empty = new(new Dictionary<Guid, GroupRealisation>(), false);

    public decimal FromIncomePln => ByGroup.Values.Sum(r => r.FromIncomePln);
}

internal static class GroupFunding
{
    private sealed record Transfer(Guid GroupId, Guid? SourceAccountId, decimal AmountPln);

    public static GroupFundingResult Compute(
        IReadOnlyList<FundingLeg> legs,
        IReadOnlyDictionary<Guid, IReadOnlyList<ContributionSource>> contributions,
        IReadOnlySet<Guid> incomeAccounts)
    {
        var transfers = new List<Transfer>();
        var incomplete = false;

        foreach (var pair in legs.Where(l => l.TransferId is not null).GroupBy(l => l.TransferId))
        {
            var members = pair.ToList();
            if (members.Count != 2 || members.Any(m => m.Type is TransactionType.Dividend or TransactionType.Interest))
            {
                continue;
            }

            var receiving = members.FirstOrDefault(m => m.Type is TransactionType.Deposit or TransactionType.Buy);
            var paying = members.FirstOrDefault(m => m.Type is TransactionType.Withdraw or TransactionType.Sell);
            if (receiving?.GroupId is not { } groupId || paying is null || paying.GroupId == groupId)
            {
                continue;
            }

            var amount = AmountPln(receiving.Type == TransactionType.Deposit ? receiving : paying);
            if (amount is null)
            {
                incomplete = true;
                continue;
            }

            transfers.Add(new Transfer(groupId, paying.AccountId, amount.Value));
        }

        var byGroup = new Dictionary<Guid, GroupRealisation>();
        foreach (var group in transfers.GroupBy(t => t.GroupId))
        {
            var planned = contributions.GetValueOrDefault(group.Key) ?? [];
            var bySource = group
                .GroupBy(t => t.SourceAccountId)
                .Select(g => (Account: g.Key, Transferred: g.Sum(t => t.AmountPln)));

            var realisedByAccount = 0m;
            var incomeLeftover = 0m;
            var fromIncome = 0m;
            foreach (var (account, transferred) in bySource.ToList())
            {
                var isIncome = account is { } id && incomeAccounts.Contains(id);
                var contributed = account is null ? 0m : planned.Where(c => c.AccountId == account).Sum(c => c.AmountPln);
                var realised = Math.Min(contributed, transferred);
                realisedByAccount += realised;
                if (isIncome)
                {
                    fromIncome += transferred;
                    incomeLeftover += transferred - realised;
                }
            }

            var withoutAccount = planned.Where(c => c.AccountId is null).Sum(c => c.AmountPln);
            var realisedWithoutAccount = Math.Min(withoutAccount, incomeLeftover);

            byGroup[group.Key] = new GroupRealisation(
                group.Sum(t => t.AmountPln),
                fromIncome,
                incomeLeftover - realisedWithoutAccount,
                realisedByAccount + realisedWithoutAccount);
        }

        return new GroupFundingResult(byGroup, incomplete);
    }

    private static decimal? AmountPln(FundingLeg leg)
    {
        if (leg.AmountPln is { } amountPln)
        {
            return amountPln;
        }

        return leg.FxRateToPln is { } rate
            ? Math.Round(leg.Quantity * leg.UnitPriceAmount * rate, 2, MidpointRounding.AwayFromZero)
            : null;
    }
}
