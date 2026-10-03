using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string LivenessEndpointPath = "/health/live";
    private const string ReadinessEndpointPath = "/health/ready";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.AddJwtAuthentication();

        builder.AddProblemDetailsWithTraceId();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();

            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("MassTransit");
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    // Without it, publish and consume spans never reach the exporter.
                    .AddSource("MassTransit")
                    .AddAspNetCoreInstrumentation(tracing =>
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(LivenessEndpointPath)
                            && !context.Request.Path.StartsWithSegments(ReadinessEndpointPath)
                    )
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    // Mapped in every environment: compose and k3s use these as restart and readiness probes.
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(LivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        });

        app.MapHealthChecks(ReadinessEndpointPath);

        return app;
    }

    // Call right after builder.Build(), before other middleware.
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        // StatusCodeSelector makes UseExceptionHandler honour BadHttpRequestException.StatusCode instead of flattening it to 500.
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = exception => exception is BadHttpRequestException badHttpRequestException
                ? badHttpRequestException.StatusCode
                : StatusCodes.Status500InternalServerError
        });

        return app;
    }
}
