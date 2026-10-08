using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.Verification;

namespace Skarbiec.MarketData.Features.AddCustomInstrument;

public sealed record AddedInstrument(CustomInstrumentResponse Instrument, bool Created);

public sealed class AddCustomInstrumentHandler(
    MarketDataDbContext dbContext,
    ITickerVerifier tickerVerifier,
    IOptions<InstrumentSearchOptions> searchOptions)
{
    public async Task<Result<AddedInstrument>> HandleAsync(AddCustomInstrumentRequest request, CancellationToken cancellationToken)
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

        // Picking a listing twice answers with the one already stored, so the client needs no conflict path.
        var existing = await dbContext.Instruments.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Source == source && i.Ticker == request.Ticker, cancellationToken);
        if (existing is not null)
        {
            return new AddedInstrument(existing.ToResponse(), Created: false);
        }

        string quoteCurrency;
        string? exchangeName = null;
        if (request.AssetClass is AssetClass.Stock or AssetClass.Etf)
        {
            var exchange = searchOptions.Value.FindBySuffix(request.Ticker);
            if (exchange is null)
            {
                return InstrumentErrors.UnsupportedExchange(request.Ticker);
            }

            quoteCurrency = exchange.Currency;
            exchangeName = exchange.Name;
        }
        else
        {
            quoteCurrency = request.QuoteCurrency!;
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
            QuoteCurrency = quoteCurrency.ToUpperInvariant(),
            AssetClass = request.AssetClass,
            Exchange = exchangeName,
            VerificationStatus = outcome == TickerVerificationOutcome.Exists
                ? InstrumentVerificationStatus.Verified
                : InstrumentVerificationStatus.Unverified,
        };

        dbContext.Instruments.Add(instrument);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AddedInstrument(instrument.ToResponse(), Created: true);
    }
}
