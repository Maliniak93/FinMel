using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Deposits.GetDeposit;

public static class GetDepositEndpoint
{
    public static IEndpointRouteBuilder MapGetDepositEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/deposits");

        group.MapGet("{assetId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<DepositResponse>, ProblemHttpResult>> HandleAsync(
        Guid assetId, GetDepositHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(assetId, cancellationToken)).ToHttpResult();
}
