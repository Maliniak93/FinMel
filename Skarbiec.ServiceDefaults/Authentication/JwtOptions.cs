namespace Skarbiec.ServiceDefaults.Authentication;

/// <summary>
/// Bound from the "Jwt" configuration section: user-secrets locally, <c>.env</c> on the VPS,
/// never committed (ADR-005).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public required string Issuer { get; init; }

    public required string Audience { get; init; }

    public required string SigningKey { get; init; }
}
