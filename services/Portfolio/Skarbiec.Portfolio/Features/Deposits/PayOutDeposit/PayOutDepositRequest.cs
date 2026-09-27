namespace Skarbiec.Portfolio.Features.Deposits.PayOutDeposit;

/// <summary>
/// Pays a settled deposit's whole balance out to a Cash asset (deposit-payout-to-cash). The amount is
/// never sent — a payout always moves everything. The date rule needs the deposit and today's
/// Europe/Warsaw date (<c>SettledOn ≤ Date ≤ today</c>), so the handler checks it.
/// </summary>
public sealed record PayOutDepositRequest
{
    /// <summary>One of the user's same-currency Cash assets in an active portfolio (<c>transfer-candidates</c>).</summary>
    public required Guid DestinationAssetId { get; init; }

    public required DateOnly Date { get; init; }
}
