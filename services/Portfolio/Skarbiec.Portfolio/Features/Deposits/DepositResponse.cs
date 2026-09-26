using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits;

/// <summary>A term deposit's read-time status (term-deposits).</summary>
public enum DepositStatus
{
    Active,

    /// <summary>The maturity date is on or before today's Europe/Warsaw date.</summary>
    Due,

    /// <summary>The deposit has been settled (term-deposits-settlement) — wins over Due and Active.</summary>
    Settled,
}

/// <summary>The totals of <see cref="DepositProjection"/> — the per-period breakdown stays server-side.</summary>
public sealed record DepositProjectionResponse
{
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
    public required decimal FinalAmount { get; init; }
    public required decimal NetProfitPercent { get; init; }
}

public sealed record DepositResponse
{
    public required Guid AssetId { get; init; }
    public required Guid PortfolioId { get; init; }
    public required string PortfolioName { get; init; }
    public required bool PortfolioIsArchived { get; init; }
    public required string Name { get; init; }
    public string? BankName { get; init; }
    public required string Currency { get; init; }
    public required decimal Principal { get; init; }
    public required DateOnly StartDate { get; init; }
    public required int TermLength { get; init; }
    public required DepositTermUnit TermUnit { get; init; }
    public required DateOnly MaturityDate { get; init; }
    public required decimal AnnualInterestRatePercent { get; init; }
    public required DepositCapitalization Capitalization { get; init; }
    public required bool TaxExempt { get; init; }
    public required decimal EarlyBreakInterestLossPercent { get; init; }
    public required DepositProjectionResponse Projection { get; init; }
    public required DepositStatus Status { get; init; }

    /// <summary>The settlement (term-deposits-settlement) — all three <see langword="null"/> until the deposit is settled.</summary>
    public DateOnly? SettledOn { get; init; }

    public decimal? SettledGrossInterest { get; init; }
    public decimal? SettledTax { get; init; }
}

public static class DepositMappingExtensions
{
    /// <summary>
    /// The projection is computed here, at read time, from the stored terms — never stored itself.
    /// <paramref name="today"/> is the Europe/Warsaw date (<see cref="WarsawCalendar.Today"/>).
    /// </summary>
    public static DepositResponse ToResponse(
        this TermDeposit terms, Asset asset, string portfolioName, bool portfolioIsArchived, DateOnly today)
    {
        var projection = DepositInterestMath.Project(new DepositTerms
        {
            Principal = terms.Principal,
            StartDate = terms.StartDate,
            TermLength = terms.TermLength,
            TermUnit = terms.TermUnit,
            AnnualInterestRatePercent = terms.AnnualInterestRatePercent,
            Capitalization = terms.Capitalization,
            TaxExempt = terms.TaxExempt
        });

        return new DepositResponse
        {
            AssetId = asset.Id,
            PortfolioId = asset.PortfolioId,
            PortfolioName = portfolioName,
            PortfolioIsArchived = portfolioIsArchived,
            Name = asset.Name,
            BankName = terms.BankName,
            Currency = asset.Currency,
            Principal = terms.Principal,
            StartDate = terms.StartDate,
            TermLength = terms.TermLength,
            TermUnit = terms.TermUnit,
            MaturityDate = terms.MaturityDate,
            AnnualInterestRatePercent = terms.AnnualInterestRatePercent,
            Capitalization = terms.Capitalization,
            TaxExempt = terms.TaxExempt,
            EarlyBreakInterestLossPercent = terms.EarlyBreakInterestLossPercent,
            Projection = new DepositProjectionResponse
            {
                GrossInterest = projection.GrossInterest,
                Tax = projection.Tax,
                NetInterest = projection.NetInterest,
                FinalAmount = projection.FinalAmount,
                NetProfitPercent = projection.NetProfitPercent
            },
            Status = terms.SettledOn is not null
                ? DepositStatus.Settled
                : terms.MaturityDate <= today ? DepositStatus.Due : DepositStatus.Active,
            SettledOn = terms.SettledOn,
            SettledGrossInterest = terms.SettledGrossInterest,
            SettledTax = terms.SettledTax
        };
    }
}
