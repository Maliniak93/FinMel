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

    /// <summary>
    /// The currency lock (ADR-026) answers like DataAnnotations validation does: a 400 whose
    /// <c>errors</c> dictionary carries the message under <c>Currency</c>, so the asset dialog's
    /// <c>applyFieldErrors</c> shows it on that control. Still stamped with <c>errorCode</c> (ADR-017).
    /// </summary>
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
