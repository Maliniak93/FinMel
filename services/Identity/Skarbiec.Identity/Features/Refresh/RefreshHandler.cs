using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Security;

namespace Skarbiec.Identity.Features.Refresh;

public sealed record RefreshResult
{
    public required RefreshResponse Response { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset RefreshTokenExpiresAtUtc { get; init; }
}

public sealed class RefreshHandler(
    UserManager<ApplicationUser> userManager,
    IdentityDbContext dbContext,
    AccessTokenGenerator accessTokenGenerator)
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    private static readonly Error InvalidRefreshToken =
        new("Unauthorized.InvalidRefreshToken", "The refresh token is invalid, expired, or has already been used.");

    public async Task<Result<RefreshResult>> HandleAsync(string? rawRefreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(rawRefreshToken))
        {
            return InvalidRefreshToken;
        }

        var tokenHash = RefreshTokenFactory.Hash(rawRefreshToken);
        var token = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (token is null || token.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return InvalidRefreshToken;
        }

        if (token.RevokedAtUtc is not null)
        {
            // Reuse of an already-rotated/revoked token: someone other than the legitimate holder
            // may have it, so the whole descendant chain is revoked (ADR-005 rotation strategy).
            await RevokeChainAsync(token, cancellationToken);
            return InvalidRefreshToken;
        }

        // UserManager<TUser> predates CancellationToken support and has no overload to forward it to.
        var user = await userManager.FindByIdAsync(token.UserId.ToString());
        if (user is null)
        {
            return InvalidRefreshToken;
        }

        var now = DateTimeOffset.UtcNow;
        var newToken = RefreshTokenFactory.Create();
        var newTokenEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = newToken.Hash,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(RefreshTokenLifetime)
        };

        token.RevokedAtUtc = now;
        token.ReplacedByTokenId = newTokenEntity.Id;
        dbContext.RefreshTokens.Add(newTokenEntity);

        await dbContext.SaveChangesAsync(cancellationToken);

        var accessToken = accessTokenGenerator.Generate(user);

        return new RefreshResult
        {
            Response = new RefreshResponse
            {
                AccessToken = accessToken.Value,
                ExpiresAtUtc = accessToken.ExpiresAtUtc
            },
            RefreshToken = newToken.RawValue,
            RefreshTokenExpiresAtUtc = newTokenEntity.ExpiresAtUtc
        };
    }

    private async Task RevokeChainAsync(RefreshToken reusedToken, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var current = reusedToken;

        while (true)
        {
            current.RevokedAtUtc ??= now;

            if (current.ReplacedByTokenId is not { } nextId)
            {
                break;
            }

            var next = await dbContext.RefreshTokens.SingleOrDefaultAsync(t => t.Id == nextId, cancellationToken);
            if (next is null)
            {
                break;
            }

            current = next;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
