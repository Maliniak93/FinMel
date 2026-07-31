using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.ListTransactions;

public static class ListTransactionsEndpoint
{
    public static IEndpointRouteBuilder MapListTransactionsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets/{assetId:guid}/transactions");

        group.MapGet("", HandleAsync).RequireAuthorization();

        return app;
    }

    // Defaulted params (mirrors ListPortfoliosEndpoint's includeArchived) so omitted query
    // parameters bind instead of 400ing as missing-required.
    private static async Task<Results<Ok<PagedResponse<TransactionResponse>>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid assetId, ListTransactionsHandler handler, CancellationToken cancellationToken, int page = 1, int pageSize = 20)
        => (await handler.HandleAsync(portfolioId, assetId, page, pageSize, cancellationToken)).ToHttpResult();
}
