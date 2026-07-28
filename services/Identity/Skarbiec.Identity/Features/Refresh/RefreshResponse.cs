namespace Skarbiec.Identity.Features.Refresh;

public sealed record RefreshResponse
{
    public required string AccessToken { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
}
