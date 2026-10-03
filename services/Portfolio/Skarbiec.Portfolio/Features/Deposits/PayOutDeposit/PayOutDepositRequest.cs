namespace Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;

/// <summary>Always pays out the whole balance; SettledOn ≤ Date ≤ today (Europe/Warsaw) is checked in the handler.</summary>
public sealed record PayOutDepositRequest
{
    public required Guid DestinationAssetId { get; init; }

    public required DateOnly Date { get; init; }
}
