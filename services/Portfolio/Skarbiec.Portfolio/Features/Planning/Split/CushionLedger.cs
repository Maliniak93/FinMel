namespace Skarbiec.Portfolio.Features.Planning.Split;

public static class CushionLedger
{
    // The level after the given months, oldest first: each adds the cushion's own contribution and what the split allocated to it.
    public static decimal LevelAfter(
        decimal startLevelPln, decimal targetPln, decimal monthlyContributionPln, IEnumerable<decimal> freeFundsPlnByMonth)
    {
        var level = startLevelPln;
        foreach (var freeFunds in freeFundsPlnByMonth)
        {
            var allocation = freeFunds <= 0m ? 0m : Math.Floor(Math.Min(freeFunds, Math.Max(0m, targetPln - level)));
            level += monthlyContributionPln + allocation;
        }

        return level;
    }
}
