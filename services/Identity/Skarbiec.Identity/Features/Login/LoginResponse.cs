namespace Skarbiec.Identity.Features.Login;

public sealed record LoginResponse
{
    public required string AccessToken { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
