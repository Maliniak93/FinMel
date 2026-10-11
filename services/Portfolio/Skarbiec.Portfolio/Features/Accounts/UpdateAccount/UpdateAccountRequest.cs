using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Portfolio.Features.Accounts.UpdateAccount;

public sealed record UpdateAccountRequest
{
    [Required, MaxLength(200)]
    public required string Name { get; init; }
}
