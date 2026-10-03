using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Features.GetInstrument;

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

        var latestQuote = await dbContext.PriceQuotes
            .AsNoTracking()
            .Where(q => q.InstrumentId == id)
            .OrderByDescending(q => q.Date)
            .FirstOrDefaultAsync(cancellationToken);

        return instrument.ToDetailsResponse(latestQuote);
    }
}
