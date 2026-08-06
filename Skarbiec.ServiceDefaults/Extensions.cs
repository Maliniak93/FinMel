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
using Skarbiec.ServiceDefaults.Http;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Wires OpenTelemetry, health checks, JWT auth, HTTP resilience and ProblemDetails for every
/// Skarbiec service via a single <see cref="AddServiceDefaults{TBuilder}"/> call (T0.3).
/// </summary>
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
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Needed by JwtForwardingHandler (T2.9's Portfolio->MarketData call, first internal
        // service-to-service HTTP call in the solution) to read the inbound request's bearer token.
        // Registered here so a service only needs to add the handler to its own typed client, not
        // remember this too.
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<JwtForwardingHandler>();

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
                    // MassTransit's own meter (T0.10) — harmless to register even for services
                    // that don't use messaging yet.
                    .AddMeter("MassTransit");
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    // MassTransit's send/receive/consume/outbox activities (T0.10, ADR-012) —
                    // without this, publish/consume spans never reach the exporter and a
                    // register request's trace stops at the HTTP+DB spans.
                    .AddSource("MassTransit")
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
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
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps <c>/health/live</c> (self only) and <c>/health/ready</c> (every registered check,
    /// including DB/broker checks a service adds on top). Exposed in every environment: compose
    /// and k3s use these for restart/readiness probes (T0.18/ADR-011), not just local dev.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(LivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        });

        app.MapHealthChecks(ReadinessEndpointPath);

        return app;
    }

    /// <summary>Middleware-side wiring — call right after <c>builder.Build()</c>, before other middleware.</summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        // StatusCodeSelector (.NET 9+) is what makes UseExceptionHandler() honor
        // BadHttpRequestException.StatusCode instead of flattening every exception to 500 — see
        // BadHttpRequestExceptionHandler's old XML doc / T1.1 for why this exception occurs
        // (Minimal API body binding throws it, with the correct status code already attached, only
        // in Development — which is every Skarbiec test host and local `dotnet run`).
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            StatusCodeSelector = exception => exception is BadHttpRequestException badHttpRequestException
                ? badHttpRequestException.StatusCode
                : StatusCodes.Status500InternalServerError
        });

        return app;
    }
}
