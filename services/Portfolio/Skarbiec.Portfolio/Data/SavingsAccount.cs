using Skarbiec.ServiceDefaults.Tenancy;

namespace Skarbiec.Portfolio.Data;

/// <summary>
/// The terms of a Savings-class <see cref="Asset"/> (savings-accounts), 1:1 with it: <see cref="AssetId"/>
/// is both the primary key and the FK to the asset row in this same database, like <see cref="TermDeposit"/>.
/// Written only by the AddSavingsAccount/UpdateSavingsAccount slices. The balance is
/// <see cref="Asset.Quantity"/>, moved only by the asset's ordinary Deposit/Withdraw transactions (ADR-009).
/// </summary>
public sealed class SavingsAccount : IUserOwned
{
    public required Guid AssetId { get; init; }
    public Guid UserId { get; set; }
    public string? BankName { get; set; }

    /// <summary>The current annual rate — no history (savings-accounts, out of scope).</summary>
    public required decimal AnnualInterestRatePercent { get; set; }

    /// <summary>IKE/IKZE — no Belka tax on the interest.</summary>
    public bool TaxExempt { get; set; }
}
