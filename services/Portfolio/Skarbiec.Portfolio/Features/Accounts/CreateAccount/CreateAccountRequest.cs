using System.ComponentModel.DataAnnotations;
using Skarbiec.Contracts;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.CreateAccount;

public sealed record CreateAccountRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    public required AccountType Type { get; init; }

    public required TaxWrapper Wrapper { get; init; }

    /// <summary>PLN when omitted; only a Bank account may hold another supported currency.</summary>
    [SupportedCurrency]
    public string? Currency { get; init; }

    /// <summary>Required for, and only for, a Bank account.</summary>
    public BankVariant? Variant { get; init; }

    [MaxLength(100)]
    public string? BankName { get; init; }

    /// <summary>A Bank account's yearly rate in percent, 0 when omitted.</summary>
    [Range(typeof(decimal), "0", "100")]
    public decimal? AnnualInterestRatePercent { get; init; }
}
