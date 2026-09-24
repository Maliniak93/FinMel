using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Skarbiec.ServiceDefaults.Http;

public static class InternalEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps a group for service-only endpoints at <c>/internal/&lt;prefix&gt;</c> (ADR-027): anonymous
    /// and excluded from the OpenAPI document. The path sits outside <c>/api/</c>, so the Gateway,
    /// which only routes <c>/api/&lt;service&gt;/**</c>, has no route to it — isolation comes from the
    /// network, not from a token. Only global data (no <c>UserId</c>) may be served here: no caller
    /// identity reaches these endpoints.
    /// </summary>
    /// <param name="app">The service's endpoint route builder.</param>
    /// <param name="prefix">The path under <c>/internal</c>, e.g. <c>"instruments"</c>.</param>
    public static RouteGroupBuilder MapInternalGroup(this IEndpointRouteBuilder app, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        // Explicit AllowAnonymous even with no fallback policy today: it records the intent and
        // survives a future fallback policy.
        return app.MapGroup($"/internal/{prefix.Trim('/')}")
            .AllowAnonymous()
            .ExcludeFromDescription();
    }
}
