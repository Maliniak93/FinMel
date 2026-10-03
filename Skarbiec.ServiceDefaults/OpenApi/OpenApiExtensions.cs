using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

public static class OpenApiExtensions
{
    public static TBuilder AddServiceOpenApi<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddOpenApi();

        return builder;
    }

    // Under the /api/<serviceName> prefix the Gateway already forwards; Development only, as the TS client generator is its only consumer.
    public static WebApplication MapServiceOpenApi(this WebApplication app, string serviceName)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi($"/api/{serviceName}/openapi/{{documentName}}.json");
        }

        return app;
    }
}
