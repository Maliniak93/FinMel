using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Skarbiec.ServiceDefaults.ErrorHandling;

public static class ProblemDetailsExtensions
{
    // The traceId extension lets a client correlate an error response with its trace in the Aspire dashboard.
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
