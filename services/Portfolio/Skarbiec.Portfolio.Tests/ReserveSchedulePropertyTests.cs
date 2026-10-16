using Skarbiec.Portfolio.Features.Planning.Recurrence;
using Skarbiec.Portfolio.Features.Planning.Reserves;
using Skarbiec.Portfolio.Tests.Fixtures;

namespace Skarbiec.Portfolio.Tests;

public sealed class ReserveSchedulePropertyTests
{
    private const int CaseCount = 100;
    private const int SpanMonths = 72;

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 0; seed < CaseCount; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    private sealed record Case(
        DateOnly Start,
        DateOnly? End,
        YearMonth Created,
        YearMonth SpanStart,
        (YearMonth From, ReserveTerms Terms)[] Versions,
        Random Random)
    {
        public ReservePlan Plan => ReservePlans.Plan(Start, Created, End, Versions);

        public IEnumerable<YearMonth> Span => ReservePlans.MonthsFrom(SpanStart, SpanMonths);
    }

    private static ReserveTerms RandomTerms(Random random)
    {
        var amount = random.Next(1, 10_000_001) / 100m;
        var interval = random.Next(1, 13);
        var rule = random.Next(4) == 0
            ? RecurrenceRules.LastBusinessDay(interval)
            : RecurrenceRules.Day(random.Next(1, 32), (BusinessDayShift)random.Next(3), interval);
        return new ReserveTerms(amount, rule);
    }

    private static Case RandomCase(int seed, bool singleVersion = false)
    {
        var random = new Random(seed);
        var start = new DateOnly(random.Next(2013, 2091), random.Next(1, 13), random.Next(1, 29));
        var startMonth = YearMonth.From(start);
        var created = startMonth.AddMonths(random.Next(-24, 25));
        DateOnly? end = random.Next(2) == 0 ? null : start.AddDays(random.Next(0, 2201));
        var spanStart = startMonth.AddMonths(-24);

        var versionCount = singleVersion ? 1 : random.Next(1, 4);
        var versions = new List<(YearMonth, ReserveTerms)> { (spanStart, RandomTerms(random)) };
        for (var i = 1; i < versionCount; i++)
        {
            versions.Add((spanStart.AddMonths(random.Next(1, SpanMonths)), RandomTerms(random)));
        }

        return new Case(start, end, created, spanStart, [.. versions], random);
    }

    private static Dictionary<YearMonth, IReadOnlyList<MonthReserve>> ReservesByMonth(Case c, ReservePlan plan) =>
        c.Span.ToDictionary(month => month, month => ReserveSchedule.ReservesIn(month, plan));

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Payment_FromTheCreatedMonthOn_IsCoveredToTheGrosz(int seed)
    {
        var c = RandomCase(seed);
        var plan = c.Plan;
        var reserves = ReservesByMonth(c, plan);

        foreach (var month in c.Span.Where(m => m >= c.Created))
        {
            foreach (var payment in ReserveSchedule.PaymentsIn(month, plan))
            {
                var covered = c.Span
                    .Where(m => m >= c.Created && m <= month)
                    .SelectMany(m => reserves[m])
                    .Where(r => r.NominalMonth == payment.NominalMonth)
                    .Sum(r => r.Amount);
                Assert.Equal(payment.Amount, covered);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Payment_BeforeTheCreatedMonth_HasOneReserveInItsOwnMonth(int seed)
    {
        var c = RandomCase(seed);
        var plan = c.Plan;
        var reserves = ReservesByMonth(c, plan);

        foreach (var month in c.Span.Where(m => m < c.Created))
        {
            foreach (var payment in ReserveSchedule.PaymentsIn(month, plan))
            {
                var matching = reserves
                    .SelectMany(pair => pair.Value.Select(r => (Month: pair.Key, Reserve: r)))
                    .Where(x => x.Reserve.NominalMonth == payment.NominalMonth)
                    .ToList();
                var only = Assert.Single(matching);
                Assert.Equal(month, only.Month);
                Assert.Equal(payment.Amount, only.Reserve.Amount);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void NewVersionFromAMonth_LeavesEarlierMonthsUnchanged(int seed)
    {
        var c = RandomCase(seed);
        var plan = c.Plan;
        var from = c.SpanStart.AddMonths(c.Random.Next(2, SpanMonths - 1));
        var replacement = RandomTerms(c.Random);
        var changed = new ReservePlan(c.Start, c.End, c.Created, month => month < from ? plan.TermsFor(month) : replacement);

        // A nominal month E dated in E-1 is a payment of E-1, so only months up to E-2 are independent of E's terms.
        foreach (var month in c.Span.Where(m => m < from.AddMonths(-1)))
        {
            Assert.Equal(ReserveSchedule.ReservesIn(month, plan), ReserveSchedule.ReservesIn(month, changed));
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void SingleVersion_ReservesAreNonNegativeEvenlySpreadAndInsideTheRange(int seed)
    {
        var c = RandomCase(seed, singleVersion: true);
        var plan = c.Plan;
        var lastMonth = c.SpanStart.AddMonths(SpanMonths - 1);

        var all = ReservesByMonth(c, plan).Values.SelectMany(r => r).ToList();

        foreach (var reserve in all)
        {
            Assert.True(reserve.Amount >= 0m);
            Assert.True(reserve.PaymentDate >= c.Start);
            Assert.True(c.End is null || reserve.PaymentDate <= c.End);
        }

        foreach (var window in all.GroupBy(r => r.NominalMonth).Where(g => YearMonth.From(g.First().PaymentDate) <= lastMonth))
        {
            var amounts = window.Select(r => r.Amount).ToList();
            Assert.True(amounts.Max() - amounts.Min() <= 0.01m * amounts.Count);
        }
    }
}
