using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

internal sealed record FundingLeg(
    Guid? TransferId,
    TransactionType Type,
    DateOnly Date,
    Guid? GroupId,
    decimal Quantity,
    decimal UnitPriceAmount,
    decimal? FxRateToPln,
    decimal? AmountPln);

internal sealed record GroupFundingResult(IReadOnlyDictionary<Guid, decimal> NetByGroup, bool Incomplete)
{
    public static readonly GroupFundingResult Empty = new(new Dictionary<Guid, decimal>(), false);

    public decimal TotalNetPln => NetByGroup.Values.Sum();

    public decimal NetOf(Guid groupId) => NetByGroup.GetValueOrDefault(groupId);
}

internal static class GroupFunding
{
    public static GroupFundingResult Compute(IReadOnlyList<FundingLeg> legs)
    {
        var net = new Dictionary<Guid, decimal>();
        var incomplete = false;

        void Add(Guid? groupId, decimal amount)
        {
            if (groupId is { } id)
            {
                net[id] = net.GetValueOrDefault(id) + amount;
            }
        }

        foreach (var leg in legs.Where(l => l.TransferId is null))
        {
            if (leg.GroupId is null || leg.Type is not (TransactionType.Deposit or TransactionType.Withdraw))
            {
                continue;
            }

            if (AmountPln(leg) is not { } amount)
            {
                incomplete = true;
                continue;
            }

            Add(leg.GroupId, leg.Type == TransactionType.Deposit ? amount : -amount);
        }

        foreach (var pair in legs.Where(l => l.TransferId is not null).GroupBy(l => l.TransferId))
        {
            var members = pair.ToList();
            if (members.Count != 2 || members.Any(m => m.Type is TransactionType.Dividend or TransactionType.Interest))
            {
                continue;
            }

            var receiving = members.FirstOrDefault(m => m.Type is TransactionType.Deposit or TransactionType.Buy);
            var paying = members.FirstOrDefault(m => m.Type is TransactionType.Withdraw or TransactionType.Sell);
            if (receiving is null || paying is null || receiving.GroupId == paying.GroupId)
            {
                continue;
            }

            if (AmountPln(receiving.Type == TransactionType.Deposit ? receiving : paying) is not { } amount)
            {
                incomplete = true;
                continue;
            }

            Add(receiving.GroupId, amount);
            Add(paying.GroupId, -amount);
        }

        return new GroupFundingResult(net, incomplete);
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
