using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetInstrument;

/// <summary>
/// Backs Portfolio's <c>InstrumentId</c> existence check (T2.9) — the first internal, service-to-
/// service caller of MarketData's API. Same auth as every other instrument endpoint; the caller's
/// JWT is simply forwarded (dotnet.md token passthrough), MarketData never learns it's Portfolio
/// asking on a user's behalf rather than the user directly.
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
        // case — lets Portfolio's asset list (T2.13) show last price/date/source per market asset
        // without a second, batch-only endpoint (GetLatestPricesBatch is SystemCaller-only, T2.11).
        var latestQuote = await dbContext.PriceQuotes
            .AsNoTracking()
            .Where(q => q.InstrumentId == id)
            .OrderByDescending(q => q.Date)
            .FirstOrDefaultAsync(cancellationToken);

        return instrument.ToDetailsResponse(latestQuote);
    }
}
