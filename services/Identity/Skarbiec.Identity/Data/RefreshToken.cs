namespace Skarbiec.Identity.Data;

/// <summary>Only <see cref="TokenHash"/> is ever stored — see <c>Security/RefreshTokenFactory</c>.</summary>
public sealed class RefreshToken
{
    public required Guid Id { get; init; }
    public required Guid UserId { get; init; }
    public required string TokenHash { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
}
