using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Accounts;

public static class NetContributions
{
    public readonly record struct Flow(TransactionType Type, decimal Quantity, bool IsTransferLeg, Guid? OtherLegAccountId);

    public static decimal Sum(Guid accountId, IReadOnlyCollection<Flow> flows)
        => flows
            .Where(f => !f.IsTransferLeg || f.OtherLegAccountId != accountId)
            .Sum(f => f.Type switch
            {
                TransactionType.Deposit => f.Quantity,
                TransactionType.Withdraw => -f.Quantity,
                _ => 0m
            });
}
