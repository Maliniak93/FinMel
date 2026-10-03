using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.SavingsAccounts.GetSavingsInterestPreview;

public static class GetSavingsInterestPreviewEndpoint
{
    public static IEndpointRouteBuilder MapGetSavingsInterestPreviewEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/savings-accounts");

        group.MapGet("{assetId:guid}/interest-preview", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<SavingsInterestPreviewResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid assetId, GetSavingsInterestPreviewHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(portfolioId, assetId, cancellationToken)).ToHttpResult();
}
