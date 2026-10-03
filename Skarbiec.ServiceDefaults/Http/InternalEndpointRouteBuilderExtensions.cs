using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Skarbiec.ServiceDefaults.Http;

public static class InternalEndpointRouteBuilderExtensions
{
    // Outside /api/, so the Gateway has no route to it; only global data may be served, as no caller identity arrives.
    public static RouteGroupBuilder MapInternalGroup(this IEndpointRouteBuilder app, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        // Explicit AllowAnonymous records the intent and survives a future fallback policy.
        return app.MapGroup($"/internal/{prefix.Trim('/')}")
            .AllowAnonymous()
            .ExcludeFromDescription();
    }
}
