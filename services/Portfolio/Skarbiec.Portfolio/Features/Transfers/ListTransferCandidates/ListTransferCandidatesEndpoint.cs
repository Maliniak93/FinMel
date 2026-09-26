using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Transfers.ListTransferCandidates;

public static class ListTransferCandidatesEndpoint
{
    public static IEndpointRouteBuilder MapListTransferCandidatesEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/transfer-candidates");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    // No defaults on purpose: both query parameters are required, so a missing one is a 400 from
    // parameter binding. assetClass binds from the enum's name or its int value.
    private static async Task<Results<Ok<IReadOnlyList<TransferCandidateResponse>>, ProblemHttpResult>> HandleAsync(
        string currency, AssetClass assetClass, ListTransferCandidatesHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(currency, assetClass, cancellationToken)).ToHttpResult();
}
