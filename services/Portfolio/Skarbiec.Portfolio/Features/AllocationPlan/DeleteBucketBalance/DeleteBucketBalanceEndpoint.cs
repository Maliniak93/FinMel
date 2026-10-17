using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.AllocationPlan.DeleteBucketBalance;

public static class DeleteBucketBalanceEndpoint
{
    public static IEndpointRouteBuilder MapDeleteBucketBalanceEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/allocation-buckets");

        group.MapDelete("{bucketId:guid}/balances/{month}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid bucketId, string month, DeleteBucketBalanceHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(bucketId, month, cancellationToken)).ToHttpResult();
}
