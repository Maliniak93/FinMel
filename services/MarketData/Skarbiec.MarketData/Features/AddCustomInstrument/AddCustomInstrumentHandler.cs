using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;

namespace Skarbiec.MarketData.Features.AddCustomInstrument;

/// <summary>
/// Adds a user-supplied instrument to the shared dictionary (E2 [S]) without ever calling Stooq/
/// CoinGecko inline (ADR-007). The instrument is created <see cref="InstrumentVerificationStatus.Unverified"/>
/// and handed to <see cref="IHistoryBackfillTrigger"/>, which reuses T2.7's one-off
/// <see cref="HistoryBackfillJob"/> both to backfill its history and — as its pragmatic, documented
/// stand-in for a dedicated verification call — to flip <see cref="Instrument.VerificationStatus"/>
/// to <see cref="InstrumentVerificationStatus.Verified"/>/<see cref="InstrumentVerificationStatus.Failed"/>
/// once that job actually runs, off the request path.
/// </summary>
public sealed class AddCustomInstrumentHandler(MarketDataDbContext dbContext, IHistoryBackfillTrigger backfillTrigger)
{
    public async Task<Result<CustomInstrumentResponse>> HandleAsync(AddCustomInstrumentRequest request, CancellationToken cancellationToken)
    {
        // NBP only serves its own fixed FX-table/gold endpoints (T2.3) — it has no notion of an
        // arbitrary user-supplied ticker, unlike Stooq/CoinGecko.
        if (request.Source == PriceSource.Nbp)
        {
            return InstrumentErrors.UnsupportedCustomSource(request.Source);
        }

        var alreadyExists = await dbContext.Instruments.AsNoTracking()
            .AnyAsync(i => i.Source == request.Source && i.Ticker == request.Ticker, cancellationToken);
        if (alreadyExists)
        {
            return InstrumentErrors.AlreadyExists(request.Source, request.Ticker);
        }

        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = request.Ticker,
            Name = request.Name,
            Source = request.Source,
            QuoteCurrency = request.QuoteCurrency.ToUpperInvariant(),
            AssetClass = request.AssetClass,
            VerificationStatus = InstrumentVerificationStatus.Unverified,
        };

        dbContext.Instruments.Add(instrument);
        await dbContext.SaveChangesAsync(cancellationToken);

        await backfillTrigger.EnqueueAsync(instrument.Id, cancellationToken);

        return instrument.ToResponse();
    }
}
