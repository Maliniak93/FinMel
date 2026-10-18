using Skarbiec.Portfolio.Features.TransferPlan;

namespace Skarbiec.Portfolio.Features.Planning.GetPlanningYear;

public sealed record PlanningYearResponse
{
    public required int Year { get; init; }

    /// <summary>The year of the earliest income or expense start; the current year when there is none.</summary>
    public required int FirstYear { get; init; }

    /// <summary>The year after the current Warsaw year.</summary>
    public required int LastYear { get; init; }

    public required IReadOnlyList<PlanningYearMonthResponse> Months { get; init; }

    public required PlanningYearTotalsResponse Totals { get; init; }
}

public sealed record PlanningYearMonthResponse
{
    /// <summary>The month (yyyy-MM).</summary>
    public required string Month { get; init; }

    /// <summary>False when no income and no expense covers the month; every value is then null.</summary>
    public required bool HasDefinitions { get; init; }

    public required decimal? IncomePlanPln { get; init; }

    /// <summary>PLN sum of the settled actual amounts; null for months after the current one.</summary>
    public required decimal? IncomeActualPln { get; init; }

    public required decimal? DifferencePln { get; init; }
    public required decimal? CostPln { get; init; }
    public required decimal? FreeFundsPlanPln { get; init; }
    public required decimal? FreeFundsForecastPln { get; init; }

    /// <summary>Null when no allocation plan version is in force.</summary>
    public required decimal? ToInvestPln { get; init; }

    public required TransferPlanStatus? TransferPlanStatus { get; init; }

    /// <summary>True when any occurrence of the month is overdue.</summary>
    public required bool HasOverdue { get; init; }

    /// <summary>True when a needed FX rate is missing, so the figures are incomplete.</summary>
    public required bool Incomplete { get; init; }
}

public sealed record PlanningYearTotalsResponse
{
    public required decimal IncomePlanPln { get; init; }
    public required decimal IncomeActualPln { get; init; }
    public required decimal DifferencePln { get; init; }
    public required decimal CostPln { get; init; }
    public required decimal FreeFundsPlanPln { get; init; }
    public required decimal FreeFundsForecastPln { get; init; }
    public required decimal ToInvestPln { get; init; }
}
