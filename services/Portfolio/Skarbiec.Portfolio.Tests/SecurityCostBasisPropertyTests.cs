using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Securities;

namespace Skarbiec.Portfolio.Tests;

public sealed class SecurityCostBasisPropertyTests
{
    private const int CaseCount = 200;

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
    public void Compute_AnySell_KeepsTheAverageBuyPrice(int seed)
    {
        var random = new Random(seed);
        var history = new List<Transaction>();
        var held = 0m;

        for (var day = 0; day < 12; day++)
        {
            var date = new DateOnly(2026, 1, 1).AddDays(day);
            if (held > 0 && random.Next(0, 3) == 0)
            {
                var quantity = random.Next(1, (int)held + 1);
                var before = SecurityCostBasis.Compute(history);
                history.Add(NewTransaction(TransactionType.Sell, quantity, random.Next(1, 500), date));
                var after = SecurityCostBasis.Compute(history);
                held -= quantity;

                Assert.Equal(held, after.Quantity);
                if (held > 0)
                {
                    Assert.InRange(Math.Abs(after.AverageBuyPrice!.Value - before.AverageBuyPrice!.Value), 0m, 0.0001m);
                }
            }
            else
            {
                var quantity = random.Next(1, 50);
                history.Add(NewTransaction(TransactionType.Buy, quantity, random.Next(1, 500), date));
                held += quantity;
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Compute_SequenceEndingAtZero_HasZeroCost(int seed)
    {
        var random = new Random(seed + 10_000);
        var history = new List<Transaction>();
        var held = 0m;

        for (var day = 0; day < 6; day++)
        {
            var quantity = random.Next(1, 50);
            history.Add(NewTransaction(TransactionType.Buy, quantity, random.Next(1, 500), new DateOnly(2026, 1, 1).AddDays(day)));
            held += quantity;
        }

        history.Add(NewTransaction(TransactionType.Sell, held, random.Next(1, 500), new DateOnly(2026, 2, 1)));

        var basis = SecurityCostBasis.Compute(history);

        Assert.Multiple(
            () => Assert.Equal(0m, basis.Quantity),
            () => Assert.Equal(0m, basis.CostQuote),
            () => Assert.Equal(0m, basis.CostPln),
            () => Assert.Null(basis.AverageBuyPrice));
    }

    private static Transaction NewTransaction(TransactionType type, decimal quantity, decimal unitPrice, DateOnly date) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = Guid.NewGuid(),
        Type = type,
        Quantity = quantity,
        UnitPriceAmount = unitPrice,
        FxRateToPln = 4m,
        Date = date
    };
}
