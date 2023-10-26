using Domain.Common;
using Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Abstractions;

[Route("api/[controller]")]
[Authorize]
[ApiController]
public class ApiController : ControllerBase
{
    protected readonly IMediator _mediator;

    protected ApiController(IMediator mediator) => _mediator = mediator;

    protected IActionResult HandleFailure(Result result) =>
        result switch
        {
            { IsSuccess: true } => throw new InvalidOperationException(),
            IValidationResult validationResult =>
                BadRequest(
                    CreateProblemDetails(
                        "Validation Error", StatusCodes.Status400BadRequest,
                        result.Error,
                        validationResult.Errors)),
            _ =>
                BadRequest(
                    CreateProblemDetails(
                        "Bad Request",
                        StatusCodes.Status400BadRequest,
                        result.Error))
        };

    protected IActionResult HandleFailureNoContent(Result result) =>
    result switch
    {
        { IsSuccess: true } => throw new InvalidOperationException(),
        IValidationResult validationResult =>
            BadRequest(
                CreateProblemDetails(
                    "Not found", StatusCodes.Status404NotFound,
                    result.Error,
                    validationResult.Errors)),
        _ =>
            BadRequest(
                CreateProblemDetails(
                    "Not found",
                    StatusCodes.Status404NotFound,
                    result.Error))
    };

    protected IActionResult HandleFailureUnauthorized(Result result) =>
        result switch
        {
            { IsSuccess: true } => throw new InvalidOperationException(),
            IValidationResult validationResult =>
                Unauthorized(
                    CreateProblemDetails(
                        "Validation Error", StatusCodes.Status401Unauthorized,
                        result.Error,
                        validationResult.Errors)),
            _ =>
                Unauthorized(
                    CreateProblemDetails(
                        "Unauthorized",
                        StatusCodes.Status401Unauthorized,
                        result.Error))
        };

    private static ProblemDetails CreateProblemDetails(
        string title,
        int status,
        Error error,
        Error[]? errors = null) =>
        new()
        {
            Title = title,
            Type = error.Code,
            Detail = error.Message,
            Status = status,
            Extensions = { { nameof(errors), errors } }
        };
}
