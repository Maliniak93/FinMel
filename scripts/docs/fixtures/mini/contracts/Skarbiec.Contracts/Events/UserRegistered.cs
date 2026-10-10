namespace Skarbiec.Contracts.Events;

public sealed record UserRegistered
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
}
