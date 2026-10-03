using Skarbiec.Contracts;

namespace Skarbiec.Portfolio.Features.Bonds;

public sealed record BondPeriod(int Index, DateOnly Start, DateOnly End);

public static class BondSchedule
{
    public static DateOnly MaturityDate(TreasuryBondType type, DateOnly purchaseDate) =>
        purchaseDate.AddMonths(TermMonths(type));

    // Every end is counted from the purchase date, never from the previous end, so a month-end purchase keeps its day.
    public static IReadOnlyList<BondPeriod> Periods(TreasuryBondType type, DateOnly purchaseDate)
    {
        var step = PeriodMonths(type);
        var count = TermMonths(type) / step;
        var periods = new List<BondPeriod>(count);
        var start = purchaseDate;

        for (var index = 1; index <= count; index++)
        {
            var end = purchaseDate.AddMonths(index * step);
            periods.Add(new BondPeriod(index, start, end));
            start = end;
        }

        return periods;
    }

    public static bool IsCoupon(TreasuryBondType type) =>
        type is TreasuryBondType.Ror or TreasuryBondType.Dor or TreasuryBondType.Coi;

    public static bool IsFixedRate(TreasuryBondType type) =>
        type is TreasuryBondType.Ots or TreasuryBondType.Tos;

    private static int TermMonths(TreasuryBondType type) => type switch
    {
        TreasuryBondType.Ots => 3,
        TreasuryBondType.Ror => 12,
        TreasuryBondType.Dor => 24,
        TreasuryBondType.Tos => 36,
        TreasuryBondType.Coi => 48,
        TreasuryBondType.Edo => 120,
        TreasuryBondType.Ros => 72,
        TreasuryBondType.Rod => 144,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unmapped TreasuryBondType — add it to BondSchedule."),
    };

    private static int PeriodMonths(TreasuryBondType type) => type switch
    {
        TreasuryBondType.Ots => 3,
        TreasuryBondType.Ror or TreasuryBondType.Dor => 1,
        _ => 12,
    };
}
