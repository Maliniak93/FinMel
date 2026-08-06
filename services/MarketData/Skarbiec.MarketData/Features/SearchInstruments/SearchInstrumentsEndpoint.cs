using Microsoft.AspNetCore.Http.HttpResults;

namespace Skarbiec.MarketData.Features.SearchInstruments;

public static class SearchInstrumentsEndpoint
{
    public static IEndpointRouteBuilder MapSearchInstrumentsEndpoint(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/marketdata/instruments");

        group.MapGet("/search", HandleAsync).RequireAuthorization();

        return app;
    }

    // Defaulted params (mirrors Portfolio's ListPortfoliosEndpoint/ListTransactionsEndpoint) so an
    // omitted "?q=" or "?limit=" binds instead of 400ing as missing-required.
    private static async Task<Ok<IReadOnlyList<InstrumentSearchResult>>> HandleAsync(
        SearchInstrumentsHandler handler, CancellationToken cancellationToken, string? q = null, int limit = 20)
        => TypedResults.Ok(await handler.HandleAsync(q, limit, cancellationToken));
}
