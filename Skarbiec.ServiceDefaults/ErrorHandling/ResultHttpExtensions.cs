using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.Contracts;

namespace Skarbiec.ServiceDefaults.ErrorHandling;

/// <summary>
/// Maps a failed <see cref="Result"/>/<see cref="Result{TValue}"/> to <see cref="TypedResults.Problem"/>
/// (ADR-017). <see cref="Error.Code"/> is expected to follow the "&lt;Category&gt;.&lt;Detail&gt;"
/// convention (e.g. "NotFound.User", "Conflict.DuplicateEmail") — the category before the first '.'
/// picks the HTTP status; an unrecognized or missing category falls back to 400.
/// </summary>
public static class ResultHttpExtensions
{
    public static Results<NoContent, ProblemHttpResult> ToHttpResult(this Result result) =>
        result.IsSuccess
            ? TypedResults.NoContent()
            : result.Error.ToProblem();

    public static Results<Ok<TValue>, ProblemHttpResult> ToHttpResult<TValue>(this Result<TValue> result) =>
        result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.ToProblem();

    public static ProblemHttpResult ToProblem(this Error error)
    {
        var (statusCode, title) = MapStatus(error.Code);

        return TypedResults.Problem(
            title: title,
            detail: error.Message,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["errorCode"] = error.Code });
    }

    private static (int StatusCode, string Title) MapStatus(string code)
    {
        var category = code.Split('.', 2)[0];

        return category switch
        {
            "NotFound" => (StatusCodes.Status404NotFound, "Not Found"),
            "Conflict" => (StatusCodes.Status409Conflict, "Conflict"),
            "Validation" => (StatusCodes.Status400BadRequest, "Validation Failed"),
            "Unauthorized" => (StatusCodes.Status401Unauthorized, "Unauthorized"),
            "Forbidden" => (StatusCodes.Status403Forbidden, "Forbidden"),
            "ServiceUnavailable" => (StatusCodes.Status503ServiceUnavailable, "Service Unavailable"),
            _ => (StatusCodes.Status400BadRequest, "Bad Request")
        };
    }
}
