namespace Skarbiec.Portfolio.Features.Accounts.SetContributionLimit;

public sealed record SetContributionLimitRequest
{
    public required decimal Amount { get; init; }
}
