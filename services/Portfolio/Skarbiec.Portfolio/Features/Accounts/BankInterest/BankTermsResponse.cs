using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.BankInterest;

public sealed record BankInterestSettlementResponse
{
    public required Guid SettlementId { get; init; }
    public required DateOnly PeriodStart { get; init; }
    public required DateOnly PeriodEnd { get; init; }
    public required decimal GrossInterest { get; init; }
    public required decimal Tax { get; init; }
    public required decimal NetInterest { get; init; }
}

public sealed record BankTermsResponse
{
    public required BankVariant Variant { get; init; }
    public string? BankName { get; init; }
    public required decimal AnnualInterestRatePercent { get; init; }

    public required bool InterestDue { get; init; }

    /// <summary>Ended, unsettled months carrying interest; 0 when InterestDue is false.</summary>
    public required int DuePeriodCount { get; init; }

    public BankInterestSettlementResponse? LastSettlement { get; init; }

    /// <summary>The sum of the stored settlements' net interest, in the account's currency.</summary>
    public required decimal InterestCreditedNet { get; init; }
}
