using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.TransferPlan;
using static Skarbiec.Portfolio.Tests.Fixtures.TransferPlanInputs;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferPlanCalculatorTests
{
    private static List<(Guid BucketId, string? Group, decimal Amount, TransferMarker Marker)> Summary(
        TransferPlanResult result, Guid? bucketId = null) =>
        result.Rows
            .Where(r => bucketId is null || r.BucketId == bucketId)
            .Select(r => (r.BucketId, r.GroupName, r.Amount, r.Marker))
            .ToList();

    [Fact]
    public void WorkedExample()
    {
        var result = TransferPlanCalculator.Compute(WorkedBaskets(), WorkedGroups(), WorkedValues(), 100);

        Assert.Equal(
            [
                (IkeId, "Akcje/ETF", 2_260m, TransferMarker.AboveTarget),
                (IkzeId, "Obligacje", 2_304m, TransferMarker.None),
                (InvestmentsId, "Obligacje", 1_300.27m, TransferMarker.None),
                (InvestmentsId, "Kruszce", 679m, TransferMarker.None)
            ],
            Summary(result));
        Assert.Equal(
            [
                ("Akcje/ETF", 60.4m, 59.5m, 50m),
                ("Obligacje", 33.0m, 33.7m, 40m),
                ("Kruszce", 6.6m, 6.7m, 10m)
            ],
            result.Groups.Select(g => (g.Name, g.CurrentPercent, g.AfterPercent, g.TargetPercent)));
    }

    [Fact]
    public void Minimum()
    {
        var single = new List<TransferBasket> { Investments(150m, order: 0) };

        var withMinimum = TransferPlanCalculator.Compute(single, WorkedGroups(), WorkedValues(), 100);
        var withoutMinimum = TransferPlanCalculator.Compute(single, WorkedGroups(), WorkedValues(), 0);
        var belowMinimum = TransferPlanCalculator.Compute(WorkedBaskets(), WorkedGroups(), WorkedValues(), 2_000);

        Assert.Equal([(InvestmentsId, "Obligacje", 150m, TransferMarker.None)], Summary(withMinimum));
        Assert.Equal(
            [
                (InvestmentsId, "Obligacje", 102m, TransferMarker.None),
                (InvestmentsId, "Kruszce", 48m, TransferMarker.None)
            ],
            Summary(withoutMinimum));
        Assert.Equal(
            [(InvestmentsId, "Obligacje", 1_979.27m, TransferMarker.BelowMinimum)],
            Summary(belowMinimum, InvestmentsId));
    }

    [Fact]
    public void BasketOrderAndOutsideAllocation()
    {
        var groups = new List<TransferGroup> { Stocks(), Metals() };
        var values = new Dictionary<string, decimal> { ["Akcje/ETF"] = 1_000m, ["Kruszce"] = 1_000m };

        var ordered = TransferPlanCalculator.Compute(
            [Investments(500m, order: 0), Ike(500m, order: 1)], WorkedGroups(), WorkedValues(), 100);
        var noDepositGroup = TransferPlanCalculator.Compute([Bank(300m)], groups, values, 100);
        var zeroTargets = TransferPlanCalculator.Compute(
            [Ike(500m)],
            [new TransferGroup("Akcje/ETF", [AssetClass.Stock, AssetClass.Etf], 0m), new TransferGroup("Kruszce", [AssetClass.PreciousMetal], 100m)],
            values,
            100);

        Assert.Equal(IkeId, ordered.Rows[0].BucketId);
        Assert.Equal(InvestmentsId, ordered.Rows[^1].BucketId);
        Assert.Equal([(BankBucketId, (string?)null, 300m, TransferMarker.OutsideAllocation)], Summary(noDepositGroup));
        Assert.Equal([(IkeId, (string?)null, 500m, TransferMarker.OutsideAllocation)], Summary(zeroTargets));
    }
}
