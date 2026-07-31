using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features;

namespace Skarbiec.Portfolio.Tests;

/// <summary>
/// Pure unit tests on the shared recompute function (T1.3 AC) — no Testcontainers, mirrors
/// <see cref="ArchitectureTests"/>'s container-free style.
/// </summary>
public sealed class TransactionQuantityCalculatorTests
{
    [Theory]
    [InlineData(TransactionType.Buy, 4, 14)]
    [InlineData(TransactionType.Sell, 4, 6)]
    [InlineData(TransactionType.Deposit, 4, 14)]
    [InlineData(TransactionType.Withdraw, 4, 6)]
    [InlineData(TransactionType.Dividend, 4, 10)]
    [InlineData(TransactionType.Interest, 4, 10)]
    [InlineData(TransactionType.Fee, 4, 10)]
    public void Recompute_EachTransactionType_ProducesExpectedQuantity(TransactionType type, decimal quantity, decimal expected)
    {
        Transaction[] transactions =
        [
            NewTransaction(TransactionType.Buy, 10, new DateOnly(2026, 1, 1)),
            NewTransaction(type, quantity, new DateOnly(2026, 1, 2))
        ];

        var result = TransactionQuantityCalculator.Recompute(transactions);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void Recompute_EmptyHistory_ProducesZero()
    {
        var result = TransactionQuantityCalculator.Recompute([]);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value);
    }

    [Fact]
    public void Recompute_SellMoreThanPosition_Fails()
    {
        Transaction[] transactions =
        [
            NewTransaction(TransactionType.Buy, 10, new DateOnly(2026, 1, 1)),
            NewTransaction(TransactionType.Sell, 11, new DateOnly(2026, 1, 2))
        ];

        var result = TransactionQuantityCalculator.Recompute(transactions);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Recompute_WithdrawMoreThanPosition_Fails()
    {
        Transaction[] transactions =
        [
            NewTransaction(TransactionType.Deposit, 5, new DateOnly(2026, 1, 1)),
            NewTransaction(TransactionType.Withdraw, 6, new DateOnly(2026, 1, 2))
        ];

        var result = TransactionQuantityCalculator.Recompute(transactions);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Recompute_SellExactlyTheWholePosition_ProducesZero()
    {
        Transaction[] transactions =
        [
            NewTransaction(TransactionType.Buy, 10, new DateOnly(2026, 1, 1)),
            NewTransaction(TransactionType.Sell, 10, new DateOnly(2026, 1, 2))
        ];

        var result = TransactionQuantityCalculator.Recompute(transactions);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value);
    }

    [Fact]
    public void Recompute_ReplaysOutOfInputOrder_ByDateRegardlessOfListOrder()
    {
        // The Sell is listed first but dated after the Buy — replay must still go Buy-then-Sell.
        Transaction[] transactions =
        [
            NewTransaction(TransactionType.Sell, 3, new DateOnly(2026, 1, 2)),
            NewTransaction(TransactionType.Buy, 10, new DateOnly(2026, 1, 1))
        ];

        var result = TransactionQuantityCalculator.Recompute(transactions);

        Assert.True(result.IsSuccess);
        Assert.Equal(7m, result.Value);
    }

    [Fact]
    public void Recompute_ABackdatedSellThatDipsBelowZeroMidHistory_Fails()
    {
        // Final total (10 - 3 + 8 - 12 = 3) is non-negative, but the backdated Withdraw
        // (inserted between the Buy and the later Deposit) would dip the running total negative.
        Transaction[] transactions =
        [
            NewTransaction(TransactionType.Buy, 10, new DateOnly(2026, 1, 1)),
            NewTransaction(TransactionType.Withdraw, 12, new DateOnly(2026, 1, 2)),
            NewTransaction(TransactionType.Deposit, 8, new DateOnly(2026, 1, 3))
        ];

        var result = TransactionQuantityCalculator.Recompute(transactions);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Recompute_RandomSequences_NeverProducesNegativeQuantity()
    {
        var random = new Random(42);
        var allTypes = Enum.GetValues<TransactionType>();

        for (var run = 0; run < 100; run++)
        {
            var transactions = new List<Transaction>();
            for (var day = 0; day < 20; day++)
            {
                var type = allTypes[random.Next(allTypes.Length)];
                var quantity = random.Next(0, 20);
                transactions.Add(NewTransaction(type, quantity, new DateOnly(2026, 1, 1).AddDays(day)));
            }

            var result = TransactionQuantityCalculator.Recompute(transactions);

            if (result.IsSuccess)
            {
                Assert.True(result.Value >= 0);
            }
        }
    }

    private static Transaction NewTransaction(TransactionType type, decimal quantity, DateOnly date) => new()
    {
        Id = Guid.NewGuid(),
        AssetId = Guid.NewGuid(),
        Type = type,
        Quantity = quantity,
        Date = date
    };
}
