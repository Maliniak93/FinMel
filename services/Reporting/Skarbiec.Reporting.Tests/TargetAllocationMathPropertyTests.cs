using Skarbiec.Contracts;
using Skarbiec.Reporting.Data;
using Skarbiec.Reporting.Features.TargetAllocation;

namespace Skarbiec.Reporting.Tests;

public sealed class TargetAllocationMathPropertyTests
{
    private const int CaseCount = 50;

    private static readonly AssetClass[] Classes =
        [AssetClass.Cash, AssetClass.Deposit, AssetClass.Stock, AssetClass.Etf, AssetClass.Bond, AssetClass.PreciousMetal, AssetClass.BankAccount];

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 0; seed < CaseCount; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Compute_TargetValuesSumToBaseAndSharesAreRoundedQuotients(int seed)
    {
        var random = new Random(seed);
        var shuffled = Classes.OrderBy(_ => random.Next()).ToList();
        var groupCount = random.Next(1, 6);
        var groupedClasses = shuffled.Take(random.Next(groupCount, Classes.Length + 1)).ToList();

        var tenths = new int[groupCount];
        var remaining = 1000;
        for (var i = 0; i < groupCount - 1; i++)
        {
            tenths[i] = random.Next(0, remaining + 1);
            remaining -= tenths[i];
        }

        tenths[groupCount - 1] = remaining;

        var groups = new List<TargetAllocationGroup>();
        for (var i = 0; i < groupCount; i++)
        {
            var classes = groupedClasses.Where((_, index) => index % groupCount == i).ToList();
            groups.Add(new TargetAllocationGroup($"G{i}", classes, tenths[i] / 10m, random.Next(0, 101) / 10m));
        }

        var values = new Dictionary<AssetClass, decimal>();
        foreach (var assetClass in Classes)
        {
            if (random.Next(4) != 0)
            {
                values[assetClass] = random.Next(0, 10_000_000) / 100m;
            }
        }

        values[groupedClasses[0]] = random.Next(1, 10_000_000) / 100m;

        var result = TargetAllocationMath.Compute(groups, values);

        var expectedBase = groups.Sum(g => g.AssetClasses.Sum(c => values.GetValueOrDefault(c)));
        Assert.Equal(expectedBase, result.BasePln);
        Assert.Equal(expectedBase, result.Groups.Sum(g => g.TargetValuePln!.Value));
        Assert.Equal(0m, result.Groups.Sum(g => g.DeviationPln!.Value));

        for (var i = 0; i < groupCount; i++)
        {
            var group = result.Groups[i];
            Assert.InRange(Math.Abs(group.TargetValuePln!.Value - groups[i].TargetPercent * expectedBase / 100m), 0m, 0.01m);
            Assert.Equal(Math.Round(group.ValuePln / expectedBase * 100m, 1, MidpointRounding.AwayFromZero), group.CurrentPercent);
        }
    }
}
