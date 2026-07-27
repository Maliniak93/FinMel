using System.ComponentModel.DataAnnotations;

namespace Skarbiec.Identity.Features.Login;

public sealed record LoginRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}
