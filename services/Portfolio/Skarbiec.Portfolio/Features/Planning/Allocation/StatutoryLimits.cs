namespace Skarbiec.Portfolio.Features.Planning.Allocation;

public sealed record YearLimits(decimal Ike, decimal Ikze, decimal IkzeJdg, bool Estimated)
{
    public decimal For(BucketKind kind) => kind switch
    {
        BucketKind.Ike => Ike,
        BucketKind.Ikze => Ikze,
        BucketKind.IkzeJdg => IkzeJdg,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "The kind has no yearly limit.")
    };
}

public static class StatutoryLimits
{
    private const int FirstKnownYear = 2024;
    private const int LastKnownYear = 2026;

    private static readonly YearLimits[] Known =
    [
        new(23_472m, 9_388.80m, 14_083.20m, false),
        new(26_019m, 10_407.60m, 15_611.40m, false),
        new(28_260m, 11_304m, 16_956m, false)
    ];

    public static YearLimits For(int year)
    {
        if (year is >= FirstKnownYear and <= LastKnownYear)
        {
            return Known[year - FirstKnownYear];
        }

        return Known[year < FirstKnownYear ? 0 : Known.Length - 1] with { Estimated = true };
    }
}
