namespace Skarbiec.MarketData.Features.GetLatestPrices;

public static class GetLatestPricesEndpoint
{
    public static IEndpointRouteBuilder MapGetLatestPricesEndpoint(this IEndpointRouteBuilder app)
    {
        var internalGroup = app.MapInternalGroup("prices");

        internalGroup.MapPost("/latest-batch", HandleAsync);

        return app;
    }
}
