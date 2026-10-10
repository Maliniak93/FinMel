namespace Skarbiec.Reporting.Features.GetSnapshot;

public static class GetSnapshotEndpoint
{
    public static IEndpointRouteBuilder MapGetSnapshotEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reporting/snapshots");

        group.MapGet("/latest", HandleAsync).RequireAuthorization();

        return app;
    }
}
