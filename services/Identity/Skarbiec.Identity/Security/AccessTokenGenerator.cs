using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Skarbiec.Identity.Data;
using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.Identity.Security;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Mints the 15-minute access token (ADR-005) signed with the same key ServiceDefaults'
/// JWT bearer handler validates against, so a token minted here round-trips through any
/// service's protected endpoints without extra configuration.
/// </summary>
public sealed class AccessTokenGenerator(IOptions<JwtOptions> jwtOptions)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public AccessToken Generate(ApplicationUser user)
    {
        var options = jwtOptions.Value;
        var now = DateTimeOffset.UtcNow;
        var expiresAtUtc = now.Add(Lifetime);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!)
        };

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAtUtc.UtcDateTime,
            signingCredentials: signingCredentials);

        var value = new JwtSecurityTokenHandler().WriteToken(token);

        return new AccessToken(value, expiresAtUtc);
    }
}
