using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Identity.Features.Register;

public sealed record RegisterRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }

    [Required, MaxLength(200)]
    public required string DisplayName { get; init; }
}
