using Microsoft.AspNetCore.Identity;
using Skarbiec.Contracts;
using Skarbiec.Identity.Data;
using Skarbiec.Identity.Security;

namespace Skarbiec.Identity.Features.Login;

public sealed record LoginResult
{
    public required LoginResponse Response { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset RefreshTokenExpiresAtUtc { get; init; }
}

public sealed class LoginHandler(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IdentityDbContext dbContext,
    AccessTokenGenerator accessTokenGenerator)
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    // Same error for "no such user" and "wrong password" — the AC requires not revealing which
    // part of the credentials was wrong (also covers the locked-out case via CheckPasswordSignInAsync).
    private static readonly Error InvalidCredentials =
        new("Unauthorized.InvalidCredentials", "Invalid email or password.");

    public async Task<Result<LoginResult>> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return InvalidCredentials;
        }

        // UserManager/SignInManager predate CancellationToken support and have no overload to forward it to.
        var signInResult = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!signInResult.Succeeded)
        {
            return InvalidCredentials;
        }

        var accessToken = accessTokenGenerator.Generate(user);
        var refreshToken = RefreshTokenFactory.Create();
        var now = DateTimeOffset.UtcNow;
        var refreshTokenExpiresAtUtc = now.Add(RefreshTokenLifetime);

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshToken.Hash,
            CreatedAtUtc = now,
            ExpiresAtUtc = refreshTokenExpiresAtUtc
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new LoginResult
        {
            Response = new LoginResponse
            {
                AccessToken = accessToken.Value,
                ExpiresAtUtc = accessToken.ExpiresAtUtc
            },
            RefreshToken = refreshToken.RawValue,
            RefreshTokenExpiresAtUtc = refreshTokenExpiresAtUtc
        };
    }
}
