using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.Contracts;
using Skarbiec.ServiceDefaults.Authentication;
using Skarbiec.ServiceDefaults.ErrorHandling;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var app = builder.Build();

app.UseServiceDefaults();
app.MapDefaultEndpoints();

app.MapGet("/ping", () => TypedResults.Ok("pong"));

app.MapGet("/secure", (ICurrentUser currentUser) => TypedResults.Ok(currentUser.UserId))
    .RequireAuthorization();

app.MapGet("/sample-result/{outcome}", (string outcome) =>
{
    Result<string> result = outcome switch
    {
        "ok" => Result<string>.Success("all good"),
        "not-found" => new Error("NotFound.Sample", "Sample resource not found."),
        "conflict" => new Error("Conflict.Sample", "Sample resource already exists."),
        _ => new Error("Validation.Sample", "Unknown outcome requested.")
    };

    return result.ToHttpResult();
});

// Proves the global exception handler (genuinely unexpected failures, ADR-017) also
// produces ProblemDetails with a traceId — not just the Result-mapped path above.
app.MapGet("/boom", () =>
{
    throw new InvalidOperationException("Sample unexpected failure.");
});

app.Run();

// Exposed for Skarbiec.ServiceDefaults.Tests' WebApplicationFactory<Program>.
public partial class Program;
