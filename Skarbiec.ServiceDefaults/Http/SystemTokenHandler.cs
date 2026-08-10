using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Skarbiec.ServiceDefaults.Authentication;

namespace Skarbiec.ServiceDefaults.Http;

/// <summary>
/// Attaches a short-lived, self-minted <see cref="SystemCaller"/> JWT to every outgoing request on
/// a typed HttpClient — for background jobs/consumers with no inbound caller JWT to forward
/// (contrast <see cref="JwtForwardingHandler"/>, which needs an <c>HttpContext</c> that doesn't
/// exist outside a request pipeline). Signed with the same shared "Jwt" key every service already
/// validates against (T2.11), so it round-trips through any service's
/// <c>.RequireAuthorization(SystemCaller.PolicyName)</c> endpoints with no extra configuration.
/// Minted fresh per request rather than cached: lifetime is short and call volume here is a
/// handful of requests per daily sync run, not worth a cache.
/// </summary>
public sealed class SystemTokenHandler(IOptions<JwtOptions> jwtOptions) : DelegatingHandler
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken());
        return base.SendAsync(request, cancellationToken);
    }

    private string MintToken()
    {
        var options = jwtOptions.Value;
        var now = DateTimeOffset.UtcNow;

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: [new Claim(SystemCaller.ClaimType, SystemCaller.ClaimValue)],
            notBefore: now.UtcDateTime,
            expires: now.Add(Lifetime).UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

public static class SystemTokenHandlerExtensions
{
    public static IHttpClientBuilder AddSystemTokenHandler(this IHttpClientBuilder builder) =>
        builder.AddHttpMessageHandler<SystemTokenHandler>();
}
