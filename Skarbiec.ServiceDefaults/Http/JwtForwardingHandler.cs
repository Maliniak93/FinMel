using Microsoft.AspNetCore.Http;

namespace Skarbiec.ServiceDefaults.Http;

/// <summary>
/// Forwards the inbound request's bearer token onto an outgoing service-to-service call (token
/// passthrough, per dotnet.md "HTTP between services") — the downstream service validates the
/// token itself and reads <c>UserId</c> from its own claims, same as if the Gateway had routed the
/// call there directly.
/// </summary>
public sealed class JwtForwardingHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrEmpty(authorization))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
