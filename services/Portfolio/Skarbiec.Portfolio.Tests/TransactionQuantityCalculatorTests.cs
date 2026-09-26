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

    /// <summary>
    /// asset-transfers-deposit-funding AC-1: same-day replay puts inflows before outflows, so a
    /// same-day top-up followed by a transfer out never fails on the Guid order — here the Withdraw
    /// has the lower Id, which the old Id tie-break replayed first. A Withdraw dated the day before
    /// the Deposit still fails: the date order wins over the inflow-first rule.
    /// </summary>
    [Fact]
    public void Recompute_SameDayInflowReplaysBeforeOutflow()
    {
        var lowerId = new Guid("00000000-0000-0000-0000-000000000001");
        var higherId = new Guid("00000000-0000-0000-0000-000000000002");
        var day = new DateOnly(2026, 1, 15);
        Transaction[] sameDay =
        [
            NewTransaction(TransactionType.Withdraw, 1_000m, day, lowerId),
            NewTransaction(TransactionType.Deposit, 1_000m, day, higherId)
        ];
        Transaction[] withdrawTheDayBefore =
        [
            NewTransaction(TransactionType.Withdraw, 1_000m, day.AddDays(-1), lowerId),
            NewTransaction(TransactionType.Deposit, 1_000m, day, higherId)
        ];

        var sameDayResult = TransactionQuantityCalculator.Recompute(sameDay);
        var dayBeforeResult = TransactionQuantityCalculator.Recompute(withdrawTheDayBefore);

        Assert.True(sameDayResult.IsSuccess, sameDayResult.IsFailure ? sameDayResult.Error.Code : null);
        Assert.Equal(0m, sameDayResult.Value);
        Assert.True(dayBeforeResult.IsFailure);
    }

    /// <summary>
    /// AC-1, the Buy/Sell side of the same rule (both are quantity deltas): a same-day Sell with the
    /// lower Id still replays after the same-day Buy, whichever order the input lists them in.
    /// </summary>
    [Fact]
    public void Recompute_SameDayBuyReplaysBeforeSellRegardlessOfIdAndListOrder()
    {
        var day = new DateOnly(2026, 3, 1);
        Transaction[] transactions =
        [
            NewTransaction(TransactionType.Sell, 4m, day, new Guid("00000000-0000-0000-0000-000000000001")),
            NewTransaction(TransactionType.Buy, 10m, day, new Guid("00000000-0000-0000-0000-000000000002")),
            NewTransaction(TransactionType.Withdraw, 6m, day, new Guid("00000000-0000-0000-0000-000000000003"))
        ];

        var forward = TransactionQuantityCalculator.Recompute(transactions);
        var reversed = TransactionQuantityCalculator.Recompute(transactions.Reverse());

        Assert.True(forward.IsSuccess, forward.IsFailure ? forward.Error.Code : null);
        Assert.Equal(0m, forward.Value);
        Assert.True(reversed.IsSuccess, reversed.IsFailure ? reversed.Error.Code : null);
        Assert.Equal(0m, reversed.Value);
    }

    private static Transaction NewTransaction(TransactionType type, decimal quantity, DateOnly date, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        AssetId = Guid.NewGuid(),
        Type = type,
        Quantity = quantity,
        Date = date
    };
}
