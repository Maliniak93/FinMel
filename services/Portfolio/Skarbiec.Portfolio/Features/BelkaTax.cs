namespace Skarbiec.Portfolio.Features;

/// <summary>
/// The Belka tax on interest, shared by term deposits and savings accounts (savings-interest-settlement)
/// so both products round the same way: 19 % of the gross, rounded up to grosze (art. 63 § 1a OP);
/// 0 for an IKE/IKZE account.
/// </summary>
public static class BelkaTax
{
    private const decimal Rate = 0.19m;

    public static decimal On(decimal grossInterest, bool taxExempt) =>
        taxExempt ? 0m : Math.Ceiling(grossInterest * Rate * 100m) / 100m;
}
