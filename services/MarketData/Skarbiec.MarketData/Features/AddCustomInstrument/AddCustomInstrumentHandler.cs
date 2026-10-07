using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.Verification;

namespace Skarbiec.MarketData.Features.AddCustomInstrument;

public sealed class AddCustomInstrumentHandler(
    MarketDataDbContext dbContext, ITickerVerifier tickerVerifier, IHistoryBackfillTrigger backfillTrigger)
{
    public async Task<Result<CustomInstrumentResponse>> HandleAsync(AddCustomInstrumentRequest request, CancellationToken cancellationToken)
    {
        var source = AssetClassPriceSourceMapping.Resolve(request.AssetClass);
        if (source is null)
        {
            return InstrumentErrors.UnsupportedAssetClass(request.AssetClass);
        }

        if (source == PriceSource.GoldApi)
        {
            return InstrumentErrors.UnsupportedCustomSource(source.Value);
        }

        var alreadyExists = await dbContext.Instruments.AsNoTracking()
            .AnyAsync(i => i.Source == source && i.Ticker == request.Ticker, cancellationToken);
        if (alreadyExists)
        {
            return InstrumentErrors.AlreadyExists(source.Value, request.Ticker);
        }

        var outcome = await tickerVerifier.VerifyAsync(source.Value, request.Ticker, cancellationToken);
        if (outcome == TickerVerificationOutcome.DoesNotExist)
        {
            return InstrumentErrors.TickerNotFound(source.Value, request.Ticker);
        }

        if (outcome == TickerVerificationOutcome.Unreachable && !request.AllowUnverified)
        {
            return InstrumentErrors.ProviderUnreachable(source.Value, request.Ticker);
        }

        var instrument = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = request.Ticker,
            Name = request.Name,
            Source = source.Value,
            QuoteCurrency = request.QuoteCurrency.ToUpperInvariant(),
            AssetClass = request.AssetClass,
            VerificationStatus = outcome == TickerVerificationOutcome.Exists
                ? InstrumentVerificationStatus.Verified
                : InstrumentVerificationStatus.Unverified,
        };

        dbContext.Instruments.Add(instrument);
        await dbContext.SaveChangesAsync(cancellationToken);

        await backfillTrigger.EnqueueAsync(instrument.Id, cancellationToken);

        return instrument.ToResponse();
    }
}
