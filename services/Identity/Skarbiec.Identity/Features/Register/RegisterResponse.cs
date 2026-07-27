namespace Skarbiec.Identity.Features.Register;

public sealed record RegisterResponse
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
}
