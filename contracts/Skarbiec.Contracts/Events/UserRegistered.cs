namespace Skarbiec.Contracts.Events;

/// <summary>Published by Identity when a new user completes registration.</summary>
public sealed record UserRegistered
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
