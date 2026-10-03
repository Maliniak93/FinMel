namespace Skarbiec.Portfolio.Features;

// Rounded up to grosze, as art. 63 § 1a of the Tax Ordinance requires.
public static class BelkaTax
{
    private const decimal Rate = 0.19m;

    public static decimal On(decimal grossInterest, bool taxExempt) =>
        taxExempt ? 0m : Math.Ceiling(grossInterest * Rate * 100m) / 100m;
}
