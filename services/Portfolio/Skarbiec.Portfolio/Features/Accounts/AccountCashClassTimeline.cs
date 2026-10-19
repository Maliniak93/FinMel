using Skarbiec.Contracts;
using Skarbiec.Contracts.Events;

namespace Skarbiec.Portfolio.Features.Accounts;

public static class AccountCashClassTimeline
{
    public sealed record Trade(Guid AssetId, AssetClass AssetClass, TransactionType Type, decimal Quantity, DateOnly Date, long EntryOrder);

    public static IReadOnlyList<ClassPoint> Build(DateOnly firstCashDate, IReadOnlyCollection<Trade> trades)
    {
        var moves = trades
            .Where(t => t.Type is TransactionType.Buy or TransactionType.Sell)
            .OrderBy(t => t.Date)
            .ThenBy(t => t.EntryOrder)
            .ToList();
        var buys = moves.Where(t => t.Type == TransactionType.Buy).ToList();
        var dates = moves.Select(t => t.Date).Where(d => d > firstCashDate).Distinct().Prepend(firstCashDate);

        var held = new Dictionary<Guid, decimal>();
        var next = 0;
        var points = new List<ClassPoint>();
        foreach (var date in dates)
        {
            for (; next < moves.Count && moves[next].Date <= date; next++)
            {
                var move = moves[next];
                held[move.AssetId] = held.GetValueOrDefault(move.AssetId) + (move.Type == TransactionType.Buy ? move.Quantity : -move.Quantity);
            }

            var assetClass = buys.LastOrDefault(b => b.Date <= date && held[b.AssetId] > 0m)?.AssetClass ?? AssetClass.Cash;
            if (points.Count == 0 || points[^1].AssetClass != assetClass)
            {
                points.Add(new ClassPoint { Date = date, AssetClass = assetClass });
            }
        }

        return points;
    }
}
