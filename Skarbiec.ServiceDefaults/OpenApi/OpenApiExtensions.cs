using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Built-in .NET OpenAPI generation (<c>Microsoft.AspNetCore.OpenApi</c>, no Swashbuckle) feeding
/// the Angular TS client generator (T1.7). Every service calls both halves explicitly from its own
/// <c>Program.cs</c> — <see cref="AddServiceOpenApi{TBuilder}"/> alongside the other
/// <c>builder.Add...()</c> calls, <see cref="MapServiceOpenApi"/> alongside
/// <c>app.MapDefaultEndpoints()</c> — since, unlike the rest of <c>AddServiceDefaults</c>, mapping
/// the document needs a per-service route segment.
/// </summary>
public static class OpenApiExtensions
{
    public static TBuilder AddServiceOpenApi<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOpenApi();

        return builder;
    }

    /// <summary>
    /// Exposes the OpenAPI document under the same "/api/&lt;serviceName&gt;/..." prefix the
    /// Gateway already forwards for this service's business endpoints — no extra Gateway route or
    /// path transform needed (ADR-013). Development only: the TS client generator
    /// (<c>web/</c> <c>npm run gen:api</c>) is the only consumer; production never maps the
    /// endpoint at all, so it 404s regardless of how the Gateway is configured.
    /// </summary>
    public static WebApplication MapServiceOpenApi(this WebApplication app, string serviceName)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi($"/api/{serviceName}/openapi/{{documentName}}.json");
        }

        return app;
    }
}
