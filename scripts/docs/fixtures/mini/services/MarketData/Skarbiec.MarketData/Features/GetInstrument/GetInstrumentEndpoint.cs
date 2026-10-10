namespace Skarbiec.MarketData.Features.GetInstrument;

public static class GetInstrumentEndpoint
{
    public static IEndpointRouteBuilder MapGetInstrumentEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/marketdata/instruments");

        group.MapGet("/{id:guid}", HandleAsync).RequireAuthorization();

        app.MapInternalGroup("instruments").MapGet("/{id:guid}", HandleAsync);

        return app;
    }
}
