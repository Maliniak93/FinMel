using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetInstrument;

/// <summary>
/// Backs two routes over the same global data (ADR-027): the public, authorized
/// <c>/api/marketdata/instruments/{id}</c> the SPA reads (assets list, edit dialog pre-fill), and its
/// anonymous <c>/internal/instruments/{id}</c> twin behind Portfolio's <c>InstrumentId</c> existence
/// check (T2.9), which sends no token.
/// </summary>
public sealed class GetInstrumentHandler(MarketDataDbContext dbContext)
{
    public async Task<Result<InstrumentDetailsResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var instrument = await dbContext.Instruments.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

        if (instrument is null)
        {
            return InstrumentErrors.NotFound(id);
        }

        // Same "latest row per instrument" lookup as SearchInstrumentsHandler, single-instrument
        // case — lets the SPA's asset list show last price/date per market asset.
        var latestQuote = await dbContext.PriceQuotes
            .AsNoTracking()
            .Where(q => q.InstrumentId == id)
            .OrderByDescending(q => q.Date)
            .FirstOrDefaultAsync(cancellationToken);

        return instrument.ToDetailsResponse(latestQuote);
    }
}
