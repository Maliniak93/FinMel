using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.UpdateAsset;

public static class UpdateAssetEndpoint
{
    public static IEndpointRouteBuilder MapUpdateAssetEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/portfolios/{portfolioId:guid}/assets");

        group.MapPut("{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<AssetResponse>, ProblemHttpResult>> HandleAsync(
        Guid portfolioId, Guid id, UpdateAssetRequest request, UpdateAssetHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(portfolioId, id, request, cancellationToken);

        if (result.IsSuccess)
        {
            return TypedResults.Ok(result.Value);
        }

        return result.Error == AssetErrors.CurrencyLockedByTransactions
            ? ToCurrencyFieldProblem(result.Error)
            : result.Error.ToProblem();
    }

    // A field-keyed 400, like DataAnnotations validation, so the asset dialog shows the lock on its Currency control.
    private static ProblemHttpResult ToCurrencyFieldProblem(Error error) =>
        TypedResults.Problem(new HttpValidationProblemDetails(
            new Dictionary<string, string[]> { [nameof(UpdateAssetRequest.Currency)] = [error.Message] })
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation Failed",
            Detail = error.Message,
            Extensions = { ["errorCode"] = error.Code },
        });
}
