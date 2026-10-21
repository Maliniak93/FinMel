using Skarbiec.Portfolio.Features.Accounts.BankInterest;

namespace Skarbiec.Portfolio.Tests;

public sealed class BankInterestMathPropertyTests
{
    private const int CaseCount = 50;

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 0; seed < CaseCount; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Accrue_AnyHistory_TaxIsWithinGrossAndNetIsTheRemainder(int seed)
    {
        var (start, end, flows, rate) = RandomCase(seed);

        var accrual = BankInterestMath.Accrue(start, end, flows, rate);

        Assert.True(accrual.GrossInterest >= 0m);
        Assert.InRange(accrual.Tax, 0m, accrual.GrossInterest);
        Assert.Equal(accrual.GrossInterest - accrual.Tax, accrual.NetInterest);
        var expectedTax = Math.Ceiling(accrual.GrossInterest * 0.19m * 100m) / 100m;
        Assert.Equal(expectedTax, accrual.Tax);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Accrue_AddingADeposit_NeverLowersTheGross(int seed)
    {
        var (start, end, flows, rate) = RandomCase(seed);
        var random = new Random(seed + 10_000);
        var extraDate = start.AddDays(random.Next(0, end.DayNumber - start.DayNumber + 1));
        var extra = random.Next(1, 100_000) / 100m;
        var before = BankInterestMath.Accrue(start, end, flows, rate);

        var after = BankInterestMath.Accrue(start, end, [.. flows, new BankCashFlow(extraDate, extra)], rate);

        Assert.True(after.GrossInterest >= before.GrossInterest);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Accrue_ConstantBalance_MatchesTheClosedForm(int seed)
    {
        var random = new Random(seed);
        var start = new DateOnly(2025, 1, 1).AddMonths(random.Next(0, 36));
        var end = start.AddMonths(1).AddDays(-1);
        var days = end.DayNumber - start.DayNumber + 1;
        var balance = random.Next(1, 100_000_000) / 100m;
        var rate = random.Next(0, 1_000_001) / 10_000m;

        var accrual = BankInterestMath.Accrue(
            start, end, [new BankCashFlow(start.AddDays(-random.Next(0, 400)), balance)], rate);

        Assert.Equal(Math.Round(balance * rate * days / 36_500m, 2, MidpointRounding.AwayFromZero), accrual.GrossInterest);
        Assert.Equal(balance, accrual.AverageDailyBalance);
    }

    private static (DateOnly Start, DateOnly End, List<BankCashFlow> Flows, decimal Rate) RandomCase(int seed)
    {
        var random = new Random(seed);
        var start = new DateOnly(2025, 1, 1).AddMonths(random.Next(0, 36));
        var end = start.AddMonths(1).AddDays(-1);
        var flows = new List<BankCashFlow>();
        var balance = 0m;

        var date = start.AddDays(-random.Next(0, 60));
        var count = random.Next(1, 8);
        for (var i = 0; i < count && date <= end; i++)
        {
            // Withdraw at most what is there, so the running balance never goes below 0.
            var amount = random.Next(1, 5_000_000) / 100m;
            if (balance > 0m && random.Next(3) == 0)
            {
                amount = -Math.Min(balance, amount);
            }

            balance += amount;
            flows.Add(new BankCashFlow(date, amount));
            date = date.AddDays(random.Next(0, 10));
        }

        return (start, end, flows, random.Next(0, 1_000_001) / 10_000m);
    }
}
