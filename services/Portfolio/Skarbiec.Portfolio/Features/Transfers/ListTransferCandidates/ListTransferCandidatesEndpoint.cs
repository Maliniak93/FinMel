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

    // No defaults on purpose: the currency is required, and so is the assetClass unless accountCash or cashPlaces picks the list.
    private static async Task<Results<Ok<IReadOnlyList<TransferCandidateResponse>>, ProblemHttpResult>> HandleAsync(
        string currency,
        ListTransferCandidatesHandler handler,
        CancellationToken cancellationToken,
        AssetClass? assetClass = null,
        bool accountCash = false,
        bool cashPlaces = false)
        => (await handler.HandleAsync(currency, assetClass, accountCash, cashPlaces, cancellationToken)).ToHttpResult();
}
