using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Security;

namespace Skarbiec.Identity.Features.Logout;

public sealed class LogoutHandler(IdentityDbContext dbContext)
{
    public async Task<Result> HandleAsync(string? rawRefreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(rawRefreshToken))
        {
            // Nothing to revoke — logging out without a session is a no-op, not an error.
            return Result.Success();
        }

        var tokenHash = RefreshTokenFactory.Hash(rawRefreshToken);
        var token = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (token is not null && token.RevokedAtUtc is null)
        {
            token.RevokedAtUtc = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
