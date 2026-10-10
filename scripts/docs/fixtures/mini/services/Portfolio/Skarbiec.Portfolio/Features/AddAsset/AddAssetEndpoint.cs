namespace Skarbiec.Portfolio.Features.AddAsset;

public static class AddAssetEndpoint
{
    public static IEndpointRouteBuilder MapAddAssetEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/portfolio/assets");

        group.MapPost("/{portfolioId:guid}", HandleAsync).RequireAuthorization();

        return app;
    }
}
