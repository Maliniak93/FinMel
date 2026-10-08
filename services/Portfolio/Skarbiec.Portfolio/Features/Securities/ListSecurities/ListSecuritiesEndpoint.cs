using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Securities.ListSecurities;

public static class ListSecuritiesEndpoint
{
    public static IEndpointRouteBuilder MapListSecuritiesEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/securities");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<SecuritiesResponse>, ProblemHttpResult>> HandleAsync(
        AssetClass assetClass, ListSecuritiesHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetClass, cancellationToken)).ToHttpResult();
}
