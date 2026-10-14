using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.UpdateAccount;

public sealed record UpdateAccountRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    /// <summary>Required for, and only for, a Bank account.</summary>
    public BankVariant? Variant { get; init; }

    [MaxLength(100)]
    public string? BankName { get; init; }

    /// <summary>A Bank account's yearly rate in percent, 0 when omitted; it applies from the next unsettled period.</summary>
    [Range(typeof(decimal), "0", "100")]
    public decimal? AnnualInterestRatePercent { get; init; }
}
