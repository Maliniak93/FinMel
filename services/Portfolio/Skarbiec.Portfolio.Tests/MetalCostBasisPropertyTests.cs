using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;
using Skarbiec.Portfolio.Features.Metals;
using static Skarbiec.Portfolio.Tests.Fixtures.MetalTradeFixtures;

namespace Skarbiec.Portfolio.Tests;

public sealed class MetalCostBasisPropertyTests
{
    [Fact]
    public void Compute_SequenceEndingAtZero_HasZeroCost()
    {
        var random = new Random(704);

        for (var run = 0; run < 200; run++)
        {
            var trades = new List<Transaction>();
            var cashLegs = new List<Transaction>();
            var held = 0m;
            var day = new DateOnly(2026, 1, 1);

            for (var step = random.Next(1, 12); step > 0; step--)
            {
                day = day.AddDays(1);
                if (held == 0m || random.Next(3) > 0)
                {
                    var pieces = random.Next(1, 11);
                    var transferId = Guid.NewGuid();
                    trades.Add(Trade(TransactionType.Buy, pieces, transferId, day));
                    cashLegs.Add(CashLeg(TransactionType.Withdraw, transferId, random.Next(100, 5_001), day));
                    held += pieces;
                }
                else
                {
                    var pieces = random.Next(1, (int)held + 1);
                    trades.Add(Trade(TransactionType.Sell, pieces, Guid.NewGuid(), day));
                    held -= pieces;
                }
            }

            if (held > 0m)
            {
                trades.Add(Trade(TransactionType.Sell, held, Guid.NewGuid(), day.AddDays(1)));
            }

            var basis = MetalCostBasis.Compute(trades, cashLegs);

            Assert.Multiple(
                () => Assert.Equal(0m, basis.Quantity),
                () => Assert.Equal(0m, basis.CostPln));
        }
    }
}
