namespace Skarbiec.Portfolio.Features.Bonds.GetBond;

public static class GetBondEndpoint
{
    public static IEndpointRouteBuilder MapGetBondEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/bonds");

        group.MapGet("/{id:guid}", HandleAsync).RequireAuthorization();

        return app;
    }
}
