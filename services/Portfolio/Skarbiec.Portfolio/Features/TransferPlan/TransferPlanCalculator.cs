using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.TransferPlan;

public sealed record TransferBasket(
    Guid BucketId, string BucketName, int Order, Guid? AccountId, string? AccountName, AccountType? AccountType, decimal Amount);

public sealed record TransferGroup(string Name, IReadOnlyList<AssetClass> AssetClasses, decimal TargetPercent);

public sealed record TransferRow(Guid BucketId, Guid? AccountId, string? GroupName, decimal Amount, TransferMarker Marker);

public sealed record TransferGroupShare(string Name, decimal CurrentPercent, decimal AfterPercent, decimal TargetPercent);

public sealed record TransferPlanResult(IReadOnlyList<TransferRow> Rows, IReadOnlyList<TransferGroupShare> Groups);

public static class TransferPlanCalculator
{
    private sealed class Piece(int? group, decimal amount, TransferMarker marker)
    {
        public int? Group { get; } = group;

        public decimal Amount { get; set; } = amount;

        public TransferMarker Marker { get; set; } = marker;
    }

    public static TransferPlanResult Compute(
        IReadOnlyList<TransferBasket> baskets,
        IReadOnlyList<TransferGroup> groups,
        IReadOnlyDictionary<string, decimal> groupValues,
        int minimum)
    {
        var start = groups.Select(g => groupValues.GetValueOrDefault(g.Name)).ToArray();
        var values = (decimal[])start.Clone();
        var spendable = baskets.Where(b => b.Amount > 0m).ToList();
        var baseAfter = start.Sum() + spendable.Sum(b => b.Amount);

        var rows = new List<TransferRow>();
        foreach (var (basket, buyable) in spendable
                     .Select(b => (Basket: b, Buyable: BuyableGroups(b.AccountType, groups)))
                     .OrderBy(x => x.Buyable.Count)
                     .ThenBy(x => x.Basket.Order))
        {
            foreach (var piece in Split(basket, buyable, groups, values, baseAfter, minimum))
            {
                if (piece.Group is { } index)
                {
                    values[index] += piece.Amount;
                }

                rows.Add(new TransferRow(
                    basket.BucketId,
                    basket.AccountId,
                    piece.Group is { } group ? groups[group].Name : null,
                    piece.Amount,
                    piece.Marker));
            }
        }

        var startTotal = start.Sum();
        var shares = groups
            .Select((g, i) => new TransferGroupShare(
                g.Name,
                Percent(start[i], startTotal),
                Percent(values[i], baseAfter),
                g.TargetPercent))
            .ToList();

        return new TransferPlanResult(rows, shares);
    }

    private static decimal Percent(decimal part, decimal whole) =>
        whole > 0m ? Math.Round(part / whole * 100m, 1, MidpointRounding.AwayFromZero) : 0m;

    private static List<int> BuyableGroups(AccountType? accountType, IReadOnlyList<TransferGroup> groups)
    {
        AssetClass[]? classes = accountType switch
        {
            AccountType.Brokerage => [AssetClass.Stock, AssetClass.Etf],
            AccountType.Bonds => [AssetClass.Bond],
            AccountType.Bank => [AssetClass.Deposit],
            _ => null
        };

        return Enumerable.Range(0, groups.Count)
            .Where(i => classes is null || groups[i].AssetClasses.Any(c => classes.Contains(c)))
            .ToList();
    }

    private static List<Piece> Split(
        TransferBasket basket,
        List<int> buyable,
        IReadOnlyList<TransferGroup> groups,
        decimal[] values,
        decimal baseAfter,
        int minimum)
    {
        var amount = basket.Amount;
        var pieces = new List<Piece>();

        var shortfalls = buyable
            .Select(i => Math.Max(0m, groups[i].TargetPercent * baseAfter / 100m - values[i]))
            .ToList();
        var shortfallTotal = shortfalls.Sum();
        var targetTotal = buyable.Sum(i => groups[i].TargetPercent);

        if (buyable.Count == 0 || targetTotal == 0m)
        {
            pieces.Add(new Piece(null, amount, TransferMarker.OutsideAllocation));
        }
        else
        {
            var excess = Math.Max(0m, amount - shortfallTotal);
            for (var k = 0; k < buyable.Count; k++)
            {
                var share = shortfallTotal >= amount
                    ? amount * shortfalls[k] / shortfallTotal
                    : shortfalls[k] + excess * groups[buyable[k]].TargetPercent / targetTotal;
                var aboveTarget = shortfallTotal < amount && groups[buyable[k]].TargetPercent > 0m;
                pieces.Add(new Piece(buyable[k], Math.Floor(share), aboveTarget ? TransferMarker.AboveTarget : TransferMarker.None));
            }
        }

        var largest = pieces.MaxBy(p => p.Amount)!;
        largest.Amount += amount - pieces.Sum(p => p.Amount);
        pieces.RemoveAll(p => p.Amount == 0m);

        var merged = basket.Amount < minimum
            ? pieces.Where(p => p != largest).ToList()
            : pieces.Where(p => p != largest && p.Amount < minimum).ToList();
        foreach (var piece in merged)
        {
            largest.Amount += piece.Amount;
            if (piece.Marker == TransferMarker.AboveTarget && largest.Marker == TransferMarker.None)
            {
                largest.Marker = TransferMarker.AboveTarget;
            }

            pieces.Remove(piece);
        }

        if (basket.Amount < minimum)
        {
            largest.Marker = TransferMarker.BelowMinimum;
        }

        return pieces;
    }
}
