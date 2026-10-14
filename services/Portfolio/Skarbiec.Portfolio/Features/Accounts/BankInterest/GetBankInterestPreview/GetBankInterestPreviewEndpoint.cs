using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.Accounts.BankInterest.GetBankInterestPreview;

public static class GetBankInterestPreviewEndpoint
{
    public static IEndpointRouteBuilder MapGetBankInterestPreviewEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/accounts");

        group.MapGet("{id:guid}/interest-preview", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<Ok<BankInterestPreviewResponse>, ProblemHttpResult>> HandleAsync(
        Guid id, GetBankInterestPreviewHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(id, cancellationToken)).ToHttpResult();
}
