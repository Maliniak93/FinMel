using Microsoft.EntityFrameworkCore;
using Skarbiec.Contracts;
using Skarbiec.MarketData.Data;

namespace Skarbiec.MarketData.Sources.Verification;

// A ticker already Verified skips the external call; anything else gets one short, non-retrying attempt.
public sealed class TickerVerifier : ITickerVerifier
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly MarketDataDbContext _dbContext;
    private readonly IEnumerable<IPriceSource> _priceSources;
    private readonly ILogger<TickerVerifier> _logger;
    private readonly TimeSpan _timeout;

    public TickerVerifier(MarketDataDbContext dbContext, IEnumerable<IPriceSource> priceSources, ILogger<TickerVerifier> logger)
        : this(dbContext, priceSources, logger, DefaultTimeout)
    {
    }

    // Test seam: a test proves the timeout budget without waiting it out.
    public TickerVerifier(MarketDataDbContext dbContext, IEnumerable<IPriceSource> priceSources, ILogger<TickerVerifier> logger, TimeSpan timeout)
    {
        _dbContext = dbContext;
        _priceSources = priceSources;
        _logger = logger;
        _timeout = timeout;
    }

    public async Task<TickerVerificationOutcome> VerifyAsync(PriceSource source, string ticker, CancellationToken cancellationToken)
    {
        var alreadyVerified = await _dbContext.Instruments.AsNoTracking().AnyAsync(
            i => i.Source == source && i.Ticker == ticker && i.VerificationStatus == InstrumentVerificationStatus.Verified,
            cancellationToken);
        if (alreadyVerified)
        {
            return TickerVerificationOutcome.Exists;
        }

        var priceSource = _priceSources.FirstOrDefault(s => s.Source == source);
        if (priceSource is null)
        {
            _logger.LogWarning("TickerVerifier: no IPriceSource registered for {Source}; treating as unreachable.", source);
            return TickerVerificationOutcome.Unreachable;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        // An ephemeral probe, never persisted: every source keys off Ticker alone.
        var probe = new Instrument
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker,
            Source = source,
            QuoteCurrency = string.Empty,
            AssetClass = AssetClass.Other,
        };

        PriceFetchResult<InstrumentQuote> result;
        try
        {
            result = await priceSource.FetchLatestAsync([probe], timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own budget expired while the caller did not cancel: unreachable, not a propagated cancellation.
            _logger.LogWarning("TickerVerifier: {Source} verification for '{Ticker}' exceeded the {Timeout} budget.", source, ticker, _timeout);
            return TickerVerificationOutcome.Unreachable;
        }

        return result.Outcome switch
        {
            PriceFetchOutcome.Success => TickerVerificationOutcome.Exists,
            PriceFetchOutcome.NoData => TickerVerificationOutcome.DoesNotExist,
            PriceFetchOutcome.Error => TickerVerificationOutcome.Unreachable,
            _ => TickerVerificationOutcome.Unreachable,
        };
    }
}
