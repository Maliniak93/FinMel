using Skarbiec.Contracts;
using Skarbiec.Portfolio.Features.Accounts;

namespace Skarbiec.Portfolio.Tests;

public sealed class NetContributionsTests
{
    private static readonly Guid AccountId = Guid.NewGuid();

    private static readonly Guid OtherAccountId = Guid.NewGuid();

    [Fact]
    public void Sum_CountsUserFlowsAndLegsWithOtherLegOutsideTheAccount()
    {
        NetContributions.Flow[] flows =
        [
            new(TransactionType.Deposit, 7_000m, IsTransferLeg: false, OtherLegAccountId: null),
            new(TransactionType.Deposit, 500m, IsTransferLeg: true, OtherLegAccountId: OtherAccountId),
            new(TransactionType.Withdraw, 200m, IsTransferLeg: false, OtherLegAccountId: null),
            new(TransactionType.Withdraw, 100m, IsTransferLeg: true, OtherLegAccountId: null),
            new(TransactionType.Deposit, 50m, IsTransferLeg: true, OtherLegAccountId: null)
        ];

        var sum = NetContributions.Sum(AccountId, flows);

        Assert.Equal(7_250m, sum);
    }

    [Fact]
    public void Sum_IgnoresFlowsWhoseOtherLegIsOnTheSameAccount()
    {
        NetContributions.Flow[] flows =
        [
            new(TransactionType.Deposit, 1_000m, IsTransferLeg: false, OtherLegAccountId: null),
            new(TransactionType.Withdraw, 1_203m, IsTransferLeg: true, OtherLegAccountId: AccountId),
            new(TransactionType.Deposit, 20m, IsTransferLeg: true, OtherLegAccountId: AccountId),
            new(TransactionType.Deposit, 300m, IsTransferLeg: true, OtherLegAccountId: AccountId)
        ];

        var sum = NetContributions.Sum(AccountId, flows);

        Assert.Equal(1_000m, sum);
    }

    [Fact]
    public void Sum_TransferBetweenTwoAccounts_IsPlusForOneAndMinusForTheOther()
    {
        NetContributions.Flow[] target = [new(TransactionType.Deposit, 500m, IsTransferLeg: true, OtherLegAccountId: OtherAccountId)];
        NetContributions.Flow[] source = [new(TransactionType.Withdraw, 500m, IsTransferLeg: true, OtherLegAccountId: AccountId)];

        Assert.Multiple(
            () => Assert.Equal(500m, NetContributions.Sum(AccountId, target)),
            () => Assert.Equal(-500m, NetContributions.Sum(OtherAccountId, source)));
    }

    [Fact]
    public void Sum_NoFlows_IsZero()
    {
        Assert.Equal(0m, NetContributions.Sum(AccountId, []));
    }
}
