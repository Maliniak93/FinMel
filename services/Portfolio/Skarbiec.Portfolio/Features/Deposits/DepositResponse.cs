using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Deposits;

public enum DepositStatus
{
    Active,

    Due,

    Settled,

    PaidOut,
}

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

    public required bool IsArchived { get; init; }

    public Guid? GroupId { get; init; }

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
    public required decimal EarlyBreakInterestLossPercent { get; init; }
    public required DepositProjectionResponse Projection { get; init; }
    public required DepositStatus Status { get; init; }

    public DateOnly? SettledOn { get; init; }

    public decimal? SettledGrossInterest { get; init; }
    public decimal? SettledTax { get; init; }

    /// <summary>The Bank account the principal left; fixed once the deposit exists.</summary>
    public required Guid SourceAccountId { get; init; }

    public required string SourceAccountName { get; init; }

    /// <summary>The Bank account the capital and net interest go back to; editable until the deposit is paid out.</summary>
    public required Guid ReturnAccountId { get; init; }

    public required string ReturnAccountName { get; init; }

    public DateOnly? PaidOutOn { get; init; }

    /// <summary>Null until the deposit is paid out.</summary>
    public string? PaidOutToAccountName { get; init; }

    public required int RolloverCount { get; init; }
}

public static class DepositMappingExtensions
{
    public static DepositResponse ToResponse(
        this TermDeposit terms,
        Asset asset,
        DateOnly today,
        IReadOnlyDictionary<Guid, string> accountNames,
        DateOnly? paidOutOn)
    {
        var projection = DepositInterestMath.Project(new DepositTerms
        {
            Principal = terms.Principal,
            StartDate = terms.StartDate,
            TermLength = terms.TermLength,
            TermUnit = terms.TermUnit,
            AnnualInterestRatePercent = terms.AnnualInterestRatePercent,
            Capitalization = terms.Capitalization
        });

        var returnAccountName = accountNames.GetValueOrDefault(terms.ReturnAccountId) ?? string.Empty;

        return new DepositResponse
        {
            AssetId = asset.Id,
            IsArchived = asset.IsArchived,
            GroupId = asset.GroupId,
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
                ? paidOutOn is not null ? DepositStatus.PaidOut : DepositStatus.Settled
                : terms.MaturityDate <= today ? DepositStatus.Due : DepositStatus.Active,
            SettledOn = terms.SettledOn,
            SettledGrossInterest = terms.SettledGrossInterest,
            SettledTax = terms.SettledTax,
            SourceAccountId = terms.SourceAccountId,
            SourceAccountName = accountNames.GetValueOrDefault(terms.SourceAccountId) ?? string.Empty,
            ReturnAccountId = terms.ReturnAccountId,
            ReturnAccountName = returnAccountName,
            PaidOutOn = paidOutOn,
            PaidOutToAccountName = paidOutOn is null ? null : returnAccountName,
            RolloverCount = terms.RolloverCount
        };
    }
}
