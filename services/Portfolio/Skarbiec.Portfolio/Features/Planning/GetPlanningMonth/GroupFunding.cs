using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningMonth;

internal sealed record FundingLeg(
    Guid? TransferId,
    TransactionType Type,
    Guid? GroupId,
    decimal Quantity,
    decimal UnitPriceAmount,
    decimal? FxRateToPln,
    decimal? AmountPln);

internal sealed record GroupFundingResult(IReadOnlyDictionary<Guid, decimal> ByGroup, bool Incomplete)
{
    public static readonly GroupFundingResult Empty = new(new Dictionary<Guid, decimal>(), false);
}

internal static class GroupFunding
{
    public static GroupFundingResult Compute(IReadOnlyList<FundingLeg> legs)
    {
        var net = new Dictionary<Guid, decimal>();
        var incomplete = false;

        void Move(Guid? from, Guid? to, FundingLeg cashLeg)
        {
            if (from == to)
            {
                return;
            }

            var amount = AmountPln(cashLeg);
            if (amount is null)
            {
                incomplete = true;
                return;
            }

            if (to is { } receiving)
            {
                net[receiving] = net.GetValueOrDefault(receiving) + amount.Value;
            }

            if (from is { } paying)
            {
                net[paying] = net.GetValueOrDefault(paying) - amount.Value;
            }
        }

        foreach (var leg in legs.Where(l => l.TransferId is null))
        {
            switch (leg.Type)
            {
                case TransactionType.Deposit:
                    Move(null, leg.GroupId, leg);
                    break;
                case TransactionType.Withdraw:
                    Move(leg.GroupId, null, leg);
                    break;
            }
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
            if (receiving is null || paying is null)
            {
                continue;
            }

            var cashLeg = receiving.Type == TransactionType.Deposit ? receiving : paying;
            Move(paying.GroupId, receiving.GroupId, cashLeg);
        }

        return new GroupFundingResult(
            net.ToDictionary(entry => entry.Key, entry => Math.Max(0m, entry.Value)),
            incomplete);
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
