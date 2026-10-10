namespace Skarbiec.MarketData.Features.GetInstrumentsBatch;

public static class GetInstrumentsBatchEndpoint
{
    public static IEndpointRouteBuilder MapGetInstrumentsBatchEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapInternalGroup("instruments").MapPost("/batch", HandleAsync);

        return app;
    }
}
