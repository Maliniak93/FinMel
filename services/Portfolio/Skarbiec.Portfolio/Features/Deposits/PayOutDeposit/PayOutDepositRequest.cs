namespace Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;

/// <summary>Always pays out the whole balance to the return account; SettledOn ≤ Date ≤ today (Europe/Warsaw) is checked in the handler.</summary>
public sealed record PayOutDepositRequest
{
    public required DateOnly Date { get; init; }
}
