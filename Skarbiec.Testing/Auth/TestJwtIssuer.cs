using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.Testing.Containers;

namespace Skarbiec.Testing.Auth;

/// <summary>
/// Mints JWTs for arbitrary <c>UserId</c>s using a service's own test-host signing key, so tenancy
/// tests (T0.14) can act as "user B" without going through registration/login.
/// </summary>
public static class TestJwtIssuer
{
    public static string Issue(JwtOptions options, Guid userId, IEnumerable<Claim>? extraClaims = null, TimeSpan? lifetime = null)
    {
        var now = DateTimeOffset.UtcNow;

        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId.ToString()) };
        if (extraClaims is not null)
        {
            claims.AddRange(extraClaims);
        }

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.Add(lifetime ?? TimeSpan.FromMinutes(15)).UtcDateTime,
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Convenience overload that reads the test host's own <see cref="JwtOptions"/> (signing key,
    /// issuer, audience) so the minted token validates against that same host without the caller
    /// needing to know its configuration.
    /// </summary>
    public static string IssueAccessToken<TProgram>(
        this SkarbiecApiFactory<TProgram> factory, Guid userId, IEnumerable<Claim>? extraClaims = null, TimeSpan? lifetime = null)
        where TProgram : class
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>().Value;

        return Issue(options, userId, extraClaims, lifetime);
    }
}
