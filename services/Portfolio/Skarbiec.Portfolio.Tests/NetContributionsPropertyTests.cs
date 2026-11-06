using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Tests;

public sealed class NetContributionsPropertyTests
{
    private const int Rounds = 200;

    private static NetContributions.Flow RandomExternal(Random random)
    {
        var type = random.Next(2) == 0 ? TransactionType.Deposit : TransactionType.Withdraw;
        var amount = random.Next(1, 100_000) / 100m;
        return random.Next(3) switch
        {
            0 => new NetContributions.Flow(type, amount, IsTransferLeg: false, OtherLegAccountId: null),
            1 => new NetContributions.Flow(type, amount, IsTransferLeg: true, OtherLegAccountId: null),
            _ => new NetContributions.Flow(type, amount, IsTransferLeg: true, OtherLegAccountId: Guid.NewGuid())
        };
    }

    [Fact]
    public void InternalFlowsDoNotChangeSum()
    {
        var random = new Random(716);
        for (var round = 0; round < Rounds; round++)
        {
            var accountId = Guid.NewGuid();
            var external = Enumerable.Range(0, random.Next(0, 8)).Select(_ => RandomExternal(random)).ToList();
            var internalFlows = Enumerable.Range(0, random.Next(1, 8))
                .Select(_ => new NetContributions.Flow(
                    random.Next(2) == 0 ? TransactionType.Deposit : TransactionType.Withdraw,
                    random.Next(1, 100_000) / 100m,
                    IsTransferLeg: true,
                    OtherLegAccountId: accountId))
                .ToList();
            var mixed = external.Concat(internalFlows).OrderBy(_ => random.Next()).ToList();

            Assert.Equal(NetContributions.Sum(accountId, external), NetContributions.Sum(accountId, mixed));
        }
    }

    [Fact]
    public void TransferBetweenAccountsCancels()
    {
        var random = new Random(717);
        for (var round = 0; round < Rounds; round++)
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();
            var transfers = Enumerable.Range(0, random.Next(1, 8))
                .Select(_ => (Amount: random.Next(1, 100_000) / 100m, FirstToSecond: random.Next(2) == 0))
                .ToList();
            var firstFlows = transfers.Select(t => new NetContributions.Flow(
                t.FirstToSecond ? TransactionType.Withdraw : TransactionType.Deposit, t.Amount, IsTransferLeg: true, OtherLegAccountId: second));
            var secondFlows = transfers.Select(t => new NetContributions.Flow(
                t.FirstToSecond ? TransactionType.Deposit : TransactionType.Withdraw, t.Amount, IsTransferLeg: true, OtherLegAccountId: first));

            var total = NetContributions.Sum(first, firstFlows.ToList()) + NetContributions.Sum(second, secondFlows.ToList());

            Assert.Equal(0m, total);
        }
    }
}
