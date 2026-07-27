using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skarbiec.ServiceDefaults.ErrorHandling;

public static class ProblemDetailsExtensions
{
    /// <summary>
    /// ProblemDetails as the default error shape, stamped with a "traceId" extension so a client
    /// can correlate an error response with the matching trace in the Aspire dashboard.
    /// Combine with <c>app.UseExceptionHandler()</c> (<see cref="Extensions.UseServiceDefaults"/>)
    /// for genuinely unexpected failures; expected failures go through <see cref="ResultHttpExtensions"/>.
    /// </summary>
    public static TBuilder AddProblemDetailsWithTraceId<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = context =>
            {
                var traceId = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions["traceId"] = traceId;
            };
        });

        return builder;
    }
}
