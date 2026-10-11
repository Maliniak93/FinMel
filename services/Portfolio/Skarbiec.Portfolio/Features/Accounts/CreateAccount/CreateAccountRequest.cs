using System.ComponentModel.DataAnnotations;
using Skarbiec.Portfolio.Data;

namespace Skarbiec.Portfolio.Features.Accounts.CreateAccount;

public sealed record CreateAccountRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }

    public required AccountType Type { get; init; }

    public required TaxWrapper Wrapper { get; init; }
}
