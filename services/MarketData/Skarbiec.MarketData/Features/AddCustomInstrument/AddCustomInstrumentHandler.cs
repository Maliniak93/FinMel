using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;
using Skarbiec.MarketData.Sources;
using Skarbiec.MarketData.Sources.Verification;

namespace Skarbiec.MarketData.Features.AddCustomInstrument;

/// <summary>
/// Adds a user-supplied instrument to the shared dictionary (E2 [S]), gated by inline ticker
/// verification (ADR-018, M1.6) instead of T2.8's original always-<see cref="InstrumentVerificationStatus.Unverified"/>-
/// then-poll flow. The source is derived from <see cref="AddCustomInstrumentRequest.AssetClass"/>
/// (<see cref="AssetClassPriceSourceMapping"/>) rather than asked for, so an ETF ticker can only ever
/// be checked against Stooq and a crypto id only against CoinGecko — never the other way round.
/// </summary>
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

        // NBP only serves its own fixed FX-table/gold endpoints (T2.3) — it has no notion of an
        // arbitrary user-supplied ticker, unlike Stooq/CoinGecko. Only PreciousMetal maps here (the
        // one real gold instrument is already seeded Verified and reachable via search); this keeps
        // the rejection meaningful instead of silently trying to "verify" against an endpoint that has
        // no way to answer an arbitrary ticker.
        if (source == PriceSource.Nbp)
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
