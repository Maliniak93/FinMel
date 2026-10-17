using Microsoft.AspNetCore.Http.HttpResults;
using Skarbiec.ServiceDefaults.ErrorHandling;

namespace Skarbiec.Portfolio.Features.AllocationPlan.SetBucketBalance;

public static class SetBucketBalanceEndpoint
{
    public static IEndpointRouteBuilder MapSetBucketBalanceEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/allocation-buckets");

        group.MapPut("{bucketId:guid}/balances/{month}", HandleAsync).RequireAuthorization();

        return app;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid bucketId, string month, SetBucketBalanceRequest request, SetBucketBalanceHandler handler, CancellationToken cancellationToken)
        => (await handler.HandleAsync(bucketId, month, request, cancellationToken)).ToHttpResult();
}
