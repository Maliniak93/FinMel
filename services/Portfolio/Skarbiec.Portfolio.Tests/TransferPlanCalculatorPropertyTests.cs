using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.TransferPlan;

namespace Skarbiec.Portfolio.Tests;

public sealed class TransferPlanCalculatorPropertyTests
{
    private const int CaseCount = 50;

    private static readonly AssetClass[] Classes =
        [AssetClass.Stock, AssetClass.Etf, AssetClass.Bond, AssetClass.Deposit, AssetClass.PreciousMetal, AssetClass.Cash];

    private static readonly AccountType?[] AccountTypes = [null, AccountType.Brokerage, AccountType.Bonds, AccountType.Bank];

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 0; seed < CaseCount; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    private static List<TransferGroup> RandomGroups(Random random)
    {
        var count = random.Next(1, 5);
        var pool = Classes.OrderBy(_ => random.Next()).ToList();
        var cuts = Enumerable.Range(0, count - 1).Select(_ => random.Next(0, 1_001)).Append(0).Append(1_000).Order().ToList();
        var groups = new List<TransferGroup>();
        for (var i = 0; i < count; i++)
        {
            var take = Math.Min(random.Next(1, 3), pool.Count - (count - 1 - i));
            var classes = pool.Take(Math.Max(take, 1)).ToList();
            pool.RemoveRange(0, classes.Count);
            groups.Add(new TransferGroup($"Grupa {i}", classes, (cuts[i + 1] - cuts[i]) / 10m));
        }

        return groups;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Compute_RandomInput_RowsSumToBasketAndRespectMinimum(int seed)
    {
        var random = new Random(seed);
        var groups = RandomGroups(random);
        var values = groups.ToDictionary(g => g.Name, _ => random.Next(0, 5_000_000) / 100m);
        var baskets = Enumerable.Range(0, random.Next(1, 6))
            .Select(i =>
            {
                var type = AccountTypes[random.Next(AccountTypes.Length)];
                return new TransferBasket(
                    Guid.NewGuid(), $"Koszyk {i}", i, type is null ? null : Guid.NewGuid(), type is null ? null : $"Konto {i}", type, random.Next(1, 5_000_000) / 100m);
            })
            .ToList();
        var minimum = random.Next(0, 2_001);

        var result = TransferPlanCalculator.Compute(baskets, groups, values, minimum);

        foreach (var basket in baskets)
        {
            var rows = result.Rows.Where(r => r.BucketId == basket.BucketId).ToList();
            Assert.NotEmpty(rows);
            Assert.Equal(basket.Amount, rows.Sum(r => r.Amount));
            Assert.True(rows.Count(r => r.Amount != Math.Floor(r.Amount)) <= 1);
            Assert.All(rows, r => Assert.True(r.Amount > 0m));
            if (rows.Count > 1)
            {
                Assert.All(rows, r => Assert.True(r.Amount >= minimum));
            }
        }
    }
}
